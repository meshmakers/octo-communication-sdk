using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// <see cref="IReadSource"/> for an iteration child of <see cref="DataContextImpl"/>: a layered
/// read over the child's own <see cref="DataOverlay"/>, its alias entries, and the parent
/// fallback chain (<see cref="IDataContextFallbackSource"/>). The child context keeps its write
/// members inline and exposes <see cref="IDataContextFallbackSource"/> for nested iteration by
/// delegating to this source (see <see cref="TryGetNodeForChildFallback"/> and
/// <see cref="IsPathTombstoned"/>).
/// </summary>
internal sealed class LayeredSource : IReadSource
{
    // Polymorphic parent: either a root or another iteration-child DataContextImpl,
    // surfaced through IDataContextFallbackSource. Cast to IDataContext when we need
    // those members (Exists, GetKind); the fallback-source members are always available.
    private readonly IDataContextFallbackSource _parent;
    private readonly Dictionary<string, JsonElement> _aliases;
    private readonly DataOverlay _overlay;

    public LayeredSource(IDataContextFallbackSource parent, Dictionary<string, JsonElement> aliases, DataOverlay overlay)
    {
        _parent = parent;
        _aliases = aliases;
        _overlay = overlay;
    }

    public JsonNode? TryGetNode(string path)
    {
        // L2: Tombstoned paths shadow alias/parent — return null without fallback.
        if (_overlay.IsTombstoned(path)) return null;
        // L1: An overlay TryRead success with a null node is an authoritative
        // explicit-null write; respect it instead of falling through.
        if (_overlay.TryRead(path, out var n)) return n;
        if (TryReadAlias(path, out var aliasElem)) return JsonDetach.ToNode(aliasElem);
        // Tombstone in any ancestor short-circuits the fallback.
        if (_parent.IsPathTombstoned(path)) return null;
        return _parent.TryGetNodeForChildFallback(path);
    }

    public bool PathExists(string path)
    {
        // Child overlay is authoritative only for paths it has written. Aliases and
        // the parent are consulted as fallbacks for unrelated paths. Tombstones in
        // the overlay are authoritatively absent and short-circuit the fallback.
        if (_overlay.IsTombstoned(path)) return false;
        if (_overlay.PathExists(path)) return true;
        if (TryReadAlias(path, out _)) return true;
        // Tombstones at any ancestor short-circuit fallback so a cleared path
        // remains absent throughout the descendant chain.
        if (_parent.IsPathTombstoned(path)) return false;
        return ((IDataContext)_parent).Exists(path);
    }

    public DataKind GetKind(string path)
    {
        // Tombstoned paths shadow alias/parent — report Undefined without fallback.
        if (_overlay.IsTombstoned(path)) return DataKind.Undefined;
        if (_overlay.HasWrites && _overlay.TryRead(path, out var n))
        {
            // n is null here means the path was explicitly written as null in the
            // child overlay — report that as Null rather than falling through to
            // the alias / parent fallback (which would shadow an authoritative null).
            if (n is null) return DataKind.Null;
            return DataKindMapper.KindOf(n);
        }
        if (TryReadAlias(path, out var aliasElem)) return DataKindMapper.KindOf(aliasElem);
        if (_parent.IsPathTombstoned(path)) return DataKind.Undefined;
        return ((IDataContext)_parent).GetKind(path);
    }

    public object? GetValue(string path, bool parseDateStrings)
    {
        var node = TryGetNode(path);
        return node is JsonValue jv ? JsonScalar.ToClr(jv, parseDateStrings) : null;
    }

    // A child context has no single immutable element base: its reads compose overlay writes
    // (node), alias JsonElements, and the parent fallback (node) under L1/L2/alias/tombstone
    // precedence. Returning false routes every child read through TryGetNode, preserving the
    // layered read semantics exactly. The element-direct zero-copy win is a ROOT-context
    // optimisation (the read-heavy EDA path reads off the root base).
    public bool TryGetElement(string path, out JsonElement element)
    {
        element = default;
        return false;
    }

    public IEnumerable<DetachedMatch> Evaluate(string jsonPath)
    {
        // Build the alias-augmented root ONCE as a JsonNode and walk it directly via
        // JsonPathWalker over a NodeView. Aliases the path can never reach are pruned, so reading
        // a small "$.key.*" attribute path no longer re-materialises an unrelated large alias
        // such as "$.full" (the whole-array iteration alias).
        var root = BuildEvalRoot(jsonPath);
        var results = new List<DetachedMatch>();
        // Each match is detached via DeepClone() (no ToJsonString) — formerly the single biggest
        // allocator (NodeView.GetRawText -> JsonNode.ToJsonString, 129 MB) on the read-heavy hot path.
        foreach (var (match, canonicalPath) in JsonPathWalker.Select(new NodeView(root), jsonPath))
            results.Add(JsonDetach.Detach(match, canonicalPath));
        return results;
    }

    public void SeedAncestorsForWrite(string path)
    {
        // §5.1 invariant: a child write at a nested path must preserve parent-fallback
        // siblings. The child's overlay base is <c>{}</c>, so a naive Write("$.x.y", v)
        // lifts to an overlay shape <c>{x: {y: v}}</c>, shadowing siblings of <c>$.x</c>
        // in the parent (e.g. <c>$.x.z</c>) AND siblings of every intermediate ancestor
        // between root and the write target. This applies to every write mode that
        // touches a non-root path, not just Overwrite.
        //
        // Walk every ancestor of the write path from root downward. For each ancestor that
        // is currently absent from the overlay but available in the parent fallback (or
        // aliases), seed it with a deep clone of the parent's view so the lifted shape
        // includes all sibling keys at every intermediate level. Idempotent: ancestors
        // already in the overlay are skipped.

        // Root writes always replace the whole document — no seeding needed.
        if (path == "$" || string.IsNullOrEmpty(path)) return;

        // Build root-to-immediate-parent ancestor chain for the write target.
        var ancestors = new List<string>();
        var ancestor = CanonicalPath.GetParent(path);
        while (ancestor is not null)
        {
            ancestors.Add(ancestor);
            ancestor = CanonicalPath.GetParent(ancestor);
        }
        ancestors.Reverse(); // root → immediate parent

        foreach (var anc in ancestors)
        {
            // Skip the literal root and any ancestor already materialized in the
            // overlay — once an ancestor is in the overlay, its descendants will
            // honor any sibling keys we seeded under it.
            if (anc == "$") continue;
            if (_overlay.HasWrites && _overlay.PathExists(anc)) continue;

            // Pull the parent's view of this ancestor (alias first, then parent fallback)
            // and seed it. This deep-copies all sibling keys at this level into the
            // overlay so SetByPath's descent for the eventual write preserves them.
            JsonNode? seed = null;
            if (TryReadAlias(anc, out var aliasElem))
            {
                seed = JsonDetach.ToNode(aliasElem);
            }
            else
            {
                if (_parent.IsPathTombstoned(anc)) continue;
                var parentNode = _parent.TryGetNodeForChildFallback(anc);
                if (parentNode is not null) seed = parentNode.DeepClone();
            }

            if (seed is not null) _overlay.Write(anc, seed);
        }
    }

    /// <summary>
    /// Resolves <paramref name="path"/> through the child's full lookup chain
    /// (overlay → aliases → parent fallback) for use by a downstream child as its parent.
    /// Backs the child's <see cref="IDataContextFallbackSource.TryGetNodeForChildFallback"/>.
    /// </summary>
    public JsonNode? TryGetNodeForChildFallback(string path) => TryGetNode(path);

    /// <summary>
    /// Tombstone semantics from a descendant's perspective: this context reports the path
    /// as authoritatively absent if (a) we tombstoned it directly, or (b) the path has no
    /// entry on this context (not in overlay, not in aliases) and an ancestor tombstoned it.
    /// A real overlay write/alias on this layer shadows any ancestor tombstone.
    /// Backs the child's <see cref="IDataContextFallbackSource.IsPathTombstoned"/>.
    /// </summary>
    public bool IsPathTombstoned(string path)
    {
        if (_overlay.IsTombstoned(path)) return true;
        if (_overlay.PathExists(path)) return false;
        if (TryReadAlias(path, out _)) return false;
        return _parent.IsPathTombstoned(path);
    }

    /// <summary>
    /// Builds the child's effective evaluation document as a <see cref="JsonNode"/>:
    /// the child's <c>$</c> view (overlay writes + seeded parent fallback) with alias
    /// entries layered on top, assembled directly from nodes (no serialize/parse
    /// round-trip) and with unreachable aliases pruned.
    /// </summary>
    /// <remarks>
    /// Pruning is match-preserving: a path whose first segment is a property <c>name</c>
    /// can only descend into the top-level key <c>name</c>, so a top-level alias with a
    /// different name is unreachable and cannot change the result set. For paths whose
    /// first segment is a wildcard, index, filter, or recursive descent — or the bare
    /// root <c>$</c> — every alias remains in scope (<see cref="AliasNameForPath"/>
    /// returns <c>null</c>), matching the original all-aliases behaviour exactly.
    /// </remarks>
    private JsonObject BuildEvalRoot(string jsonPath)
    {
        var baseNode = TryGetNode("$");
        var root = baseNode is JsonObject obj ? (JsonObject)obj.DeepClone() : new JsonObject();
        if (_aliases.Count == 0)
        {
            return root;
        }

        FoldAliases(root, AliasNameForPath(jsonPath));
        return root;
    }

    /// <summary>
    /// Layers the source's top-level alias entries onto <paramref name="root"/>. When
    /// <paramref name="reachableName"/> is non-null only that single alias is folded
    /// (match-preserving pruning for a property-rooted <c>Evaluate</c>); when null every
    /// alias is folded (the bare-root / wildcard <c>Evaluate</c> case, and the full-document
    /// snapshot taken by <see cref="GetEffectiveNode"/>).
    /// </summary>
    private void FoldAliases(JsonObject root, string? reachableName)
    {
        foreach (var kvp in _aliases)
        {
            // Aliases are top-level paths like "$.foo"; assign foo := value clone.
            if (!kvp.Key.StartsWith("$.", StringComparison.Ordinal))
            {
                continue;
            }
            var name = kvp.Key.Substring(2);
            if (name.Contains('.') || name.Contains('['))
            {
                continue;
            }
            // null reachableName => path may reach any top-level key (wildcard /
            // recursive / root): keep every alias. Otherwise keep only the one the
            // path can actually descend into.
            if (reachableName is not null && !string.Equals(reachableName, name, StringComparison.Ordinal))
            {
                continue;
            }
            root[name] = JsonDetach.ToNode(kvp.Value);
        }
    }

    /// <inheritdoc />
    public JsonNode? GetEffectiveNode(string path)
    {
        // Only a "$" read needs alias folding: aliases are synthetic top-level entries that sit
        // beside the overlay base and are invisible to a plain TryGetNode("$") (an alias key like
        // "$.full" is not an ancestor of "$"). For any deeper path TryGetNode already descends
        // through the alias chain. Without this fold, a nested iteration child's "$.full" snapshot
        // (taken over this child's "$") would drop this child's own "$.full", breaking "$.full.full".
        if ((path != "$" && !string.IsNullOrEmpty(path)) || _aliases.Count == 0)
        {
            return TryGetNode(path);
        }

        var baseNode = TryGetNode("$");
        // Aliases can only be folded onto an object root; a non-object "$" view (scalar / array /
        // null) keeps its TryGetNode value unchanged (matches pre-fix behaviour for those shapes).
        if (baseNode is not JsonObject obj)
        {
            return baseNode;
        }

        var root = (JsonObject)obj.DeepClone();
        FoldAliases(root, reachableName: null); // fold ALL aliases (full effective document)
        return root;
    }

    /// <summary>
    /// Returns the single top-level property name a JSONPath can descend into, or
    /// <c>null</c> when the path's first segment is a wildcard, index, filter, or
    /// recursive descent (any top-level key may be reached). Used to prune unreachable
    /// aliases in <see cref="BuildEvalRoot"/>.
    /// </summary>
    private static string? AliasNameForPath(string jsonPath)
    {
        var expr = JsonPathParser.Parse(JsonNodePath.NormalizePathOrRelative(jsonPath));
        foreach (var segment in expr.Segments)
        {
            switch (segment)
            {
                case RootSegment:
                    continue;
                case PropertySegment p:
                    return p.Name;
                default:
                    return null;
            }
        }
        return null;
    }

    private bool TryReadAlias(string path, out JsonElement element)
    {
        // Find longest-matching alias prefix, then descend into element.
        foreach (var kvp in _aliases)
        {
            var aliasPath = kvp.Key;
            var value = kvp.Value;
            if (CanonicalPath.IsAncestor(aliasPath, path))
            {
                if (path == aliasPath) { element = value; return true; }
                var rel = path.Substring(aliasPath.Length);
                var segments = CanonicalPath.GetSegments("$" + rel);
                JsonElement cur = value;
                foreach (var seg in segments)
                {
                    if (seg.StartsWith("."))
                    {
                        var name = seg.Substring(1);
                        if (cur.ValueKind == JsonValueKind.Object && cur.TryGetProperty(name, out cur))
                        {
                            // matched a real property; cur was reassigned
                        }
                        else if (cur.ValueKind == JsonValueKind.String && RtCkIdJsonShim.IsVirtualProperty(name))
                        {
                            // CK identifier compatibility: RtCkId<T> serializes to a string
                            // (its SemanticVersionedFullName). Legacy pipeline YAML drills
                            // .SemanticVersionedFullName / .FullName expecting the legacy
                            // reflection-emitted object shape. Treat both as virtual
                            // self-aliases on a string node so those paths keep resolving
                            // without inflating the wire format. Mirrors the same shim in
                            // JsonPathWalker.SelectProperty and DataOverlay.StepInto.
                            // cur stays as the same string element.
                        }
                        else
                        {
                            element = default;
                            return false;
                        }
                    }
                    else
                    {
                        var idx = int.Parse(seg.Substring(1, seg.Length - 2));
                        if (cur.ValueKind != JsonValueKind.Array || idx >= cur.GetArrayLength()) { element = default; return false; }
                        cur = cur[idx];
                    }
                }
                element = cur;
                return true;
            }
        }
        element = default;
        return false;
    }
}
