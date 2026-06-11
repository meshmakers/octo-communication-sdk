using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Internal contract that lets an iteration-child <see cref="DataContextImpl"/> walk the
/// parent fallback chain without caring whether the immediate parent is a root context or
/// another child. Used to support nested-iteration chains where a grandchild must read
/// through the immediate (middle) child's overlay before reaching the root, so non-aliased
/// writes on the middle child remain visible to its descendants.
/// </summary>
internal interface IDataContextFallbackSource
{
    /// <summary>Resolves <paramref name="path"/> through this context's full lookup chain
    /// (overlay → aliases → parent fallback) for use by a downstream child as its parent.
    /// Returns <c>null</c> if the path is absent or has been authoritatively tombstoned
    /// at any level of the chain.</summary>
    JsonNode? TryGetNodeForChildFallback(string path);

    /// <summary>True if this context (or any ancestor) authoritatively reports
    /// <paramref name="path"/> as cleared. A descendant uses this to short-circuit its
    /// own fallback chain when the immediate parent has tombstoned the path.</summary>
    bool IsPathTombstoned(string path);
}

/// <summary>
/// Internal factory contract used by iteration nodes that manage their own parallelism
/// (e.g. <c>ForEachNode</c>, <c>ObjectIteratorNode</c>, <c>SelectByPathNode</c>) to bypass
/// the sequential <see cref="IDataContext.IterateArrayAsync(string, System.Collections.Generic.IReadOnlyList{System.ValueTuple{string, string}}, System.Func{IDataContext, System.Threading.Tasks.Task})"/>
/// API and create per-item child contexts directly. The default <see cref="IDataContext"/>
/// API stays sequential for callers that don't explicitly want parallelism.
/// </summary>
internal interface IIterationContextFactory
{
    /// <summary>
    /// Resolves the given alias source paths against the current context once, returning
    /// (alias, JsonElement) tuples that can be reused across multiple iteration children.
    /// </summary>
    IReadOnlyList<(string AliasPath, JsonElement Value)> ResolveAliasElements(
        IReadOnlyList<(string Alias, string SourcePath)> aliases);

    /// <summary>
    /// Creates a child context seeded with the given iteration <paramref name="item"/> and
    /// pre-resolved <paramref name="aliases"/>. Each call returns an independent child with
    /// its own overlay; no state is shared with siblings, so children are safe to use
    /// concurrently from different threads.
    /// </summary>
    IDataContext CreateIterationChild(IReadOnlyList<(string AliasPath, JsonElement Value)> aliases,
        JsonNode? item);
}

/// <summary>
/// Internal contract exposing the full effective <c>"$"</c> document — with any synthetic
/// iteration aliases (e.g. <c>ForEachNode</c>'s <c>"$.full"</c>) folded in — for the pipeline
/// debugger's per-node input/output snapshot. Deliberately kept OFF the public path-only
/// <see cref="IDataContext"/> surface (privileged node-infrastructure access, like
/// <see cref="IIterationContextFactory"/>): the sole caller is
/// <see cref="Nodes.NodeContext"/>'s debug capture, which invokes it only inside a
/// <c>PipelineDebugger?.</c> null-conditional, so nothing here is evaluated — and nothing is
/// allocated — when debugging is disabled.
/// </summary>
internal interface IDebugSnapshotSource
{
    /// <summary>
    /// Returns the full effective root document for a debug snapshot, folding in any synthetic
    /// top-level iteration aliases that a plain <c>Get&lt;JsonNode&gt;("$")</c>/<c>TryGetNode</c>
    /// omits on an iteration child (most notably <c>ForEachNode</c>'s <c>"$.full"</c>). Returns
    /// <c>null</c> when <c>"$"</c> is absent or explicitly null. On a root context this is exactly
    /// the plain <c>"$"</c> read (no aliases to fold).
    /// </summary>
    JsonNode? GetDebugSnapshot();
}

/// <summary>
/// Default <see cref="IDataContext"/> implementation backed by a single layered
/// <see cref="DataOverlay"/> over an <see cref="IReadSource"/>. The same class serves both
/// the root context (read base is a zero-copy <see cref="JsonElement"/> via
/// <see cref="ElementSource"/>) and iteration children (read base is the parent fallback
/// chain plus aliases via <see cref="LayeredSource"/>); the only difference is which source
/// and which constructor produced the instance. Exposes a path-only API for ETL pipeline
/// nodes; no JSON object types appear on the public surface.
/// </summary>
public sealed class DataContextImpl : IDataContext, IIterationContextFactory, IDataContextFallbackSource,
    IDebugSnapshotSource
{
    // Process-wide cached empty document, used as the read-base overlay for every iteration
    // child (children rely on aliases + parent fallback, so they never read directly from
    // this base). Previously each child allocated `JsonDocument.Parse("{}")` and dropped the
    // document on the floor, leaving its pooled UTF-8 buffer pinned until finalization. With
    // iteration creating one child per item, the per-iteration churn was real on long arrays.
    private static readonly JsonDocument EmptyObjectDocument = JsonDocument.Parse("{}");
    private static readonly JsonElement EmptyObjectElement = EmptyObjectDocument.RootElement;

    private readonly DataOverlay _overlay;
    private readonly JsonDocument? _ownedDocument;
    // Read backend for this context. Reads route through the seam; writes go straight to
    // _overlay. ElementSource serves the zero-copy JsonElement base for a root context;
    // LayeredSource serves the layered alias + parent-fallback + overlay view for a child.
    private readonly IReadSource _source;
    // Immediate parent for an iteration child, or null for a root context. Drives both the
    // public Parent property and the child's fallback chain. A child's parent is whatever
    // IDataContextFallbackSource created it (a root context or another child).
    private readonly IDataContextFallbackSource? _parent;
    private bool _disposed;

    /// <summary>
    /// Creates a new context wrapping the given <paramref name="baseElement"/>. The caller
    /// retains ownership of the underlying <see cref="JsonDocument"/>.
    /// </summary>
    public DataContextImpl(JsonElement baseElement)
    {
        _overlay = new DataOverlay(baseElement);
        _source = new ElementSource(baseElement, _overlay);
    }

    /// <summary>
    /// Creates a new context that takes ownership of the given <paramref name="document"/>.
    /// </summary>
    public DataContextImpl(JsonDocument document) : this(document.RootElement)
    {
        _ownedDocument = document;
    }

    /// <summary>
    /// Creates a new context with an empty JSON object as the base document.
    /// </summary>
    public DataContextImpl() : this(JsonDocument.Parse("{}")) { }

    /// <summary>
    /// Creates a standalone context backed by a detached <paramref name="detachedRoot"/> node
    /// (or JSON null). The node is lifted into the overlay at <c>$</c>, so every read routes
    /// through it and writes stay isolated; the context owns no <see cref="JsonDocument"/>, so
    /// <see cref="Dispose"/> is a no-op. Used to wrap an <c>Evaluate</c> match without a
    /// serialize/parse round-trip. The cached <see cref="EmptyObjectElement"/> base is never read
    /// because the overlay write makes <c>HasWrites</c> true and shadows it entirely.
    /// </summary>
    internal DataContextImpl(JsonNode? detachedRoot)
    {
        _overlay = new DataOverlay(EmptyObjectElement);
        _overlay.Write("$", detachedRoot);
        _source = new ElementSource(EmptyObjectElement, _overlay);
    }

    /// <summary>
    /// Creates an iteration child of <paramref name="parent"/>. The child's read base is
    /// empty — <paramref name="aliases"/> provide the synthetic top-level entries and the
    /// parent fallback chain covers everything else. Writes on the child stay isolated in
    /// its own overlay and never escape to the parent. A child never owns a JsonDocument
    /// (its overlay base is the shared cached empty element), so Dispose is a no-op for it.
    /// </summary>
    private DataContextImpl(IDataContextFallbackSource parent,
        IReadOnlyList<(string AliasPath, JsonElement Value)> aliases)
    {
        _parent = parent;
        var aliasMap = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kvp in aliases) aliasMap[kvp.AliasPath] = kvp.Value;

        // Reuse the process-wide cached empty document so each child doesn't allocate
        // (and orphan) its own. The child reads aliases + parent fallback; the overlay
        // base is consulted only for paths the child itself wrote.
        _overlay = new DataOverlay(EmptyObjectElement);
        _source = new LayeredSource(parent, aliasMap, _overlay);
    }

    /// <inheritdoc />
    public IDataContext? Parent => _parent as IDataContext;

    /// <inheritdoc />
    public bool Exists(string path) => _source.PathExists(path);

    /// <inheritdoc />
    public DataKind GetKind(string path) => _source.GetKind(path);

    /// <inheritdoc />
    public int Length(string path)
    {
        // Align with Keys(path)'s defaults-on-missing model: a missing or null path
        // has length 0 rather than throwing. Number / Boolean still throw because
        // "length" is genuinely undefined for those.
        return GetKind(path) switch
        {
            DataKind.Array => GetAsNode(path)?.AsArray().Count ?? 0,
            DataKind.Object => GetAsNode(path)?.AsObject().Count ?? 0,
            DataKind.String => GetAsNode(path)?.GetValue<string>().Length ?? 0,
            DataKind.Undefined => 0,
            DataKind.Null => 0,
            _ => throw new InvalidOperationException($"Length not defined for kind at '{path}'")
        };
    }

    /// <inheritdoc />
    public IEnumerable<string> Keys(string path)
    {
        var node = GetAsNode(path);
        if (node is JsonObject obj) return obj.Select(p => p.Key);
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public T? Get<T>(string path)
    {
        // Get<JsonNode> is the hot read in transform nodes (e.g. CreateUpdateInfo's
        // m.Get<JsonNode>("$")): callers want an independent mutable clone, which only the node
        // representation provides. DeepClone preserves the copy semantics of the former
        // Deserialize<JsonNode> without its serialize+reparse round-trip.
        if (typeof(T) == typeof(JsonNode))
        {
            var n = GetAsNode(path);
            return n is null ? default : (T)(object)n.DeepClone();
        }
        // Zero-copy: deserialize straight off the immutable base element (no intermediate
        // JsonNode), when the source serves one (root base, no overlay writes). Same options =>
        // same parity converters fire keyed on T; the element→node bridge this skips copies tokens
        // verbatim, so removing it changes no numeric/null/id semantics — it only deletes the
        // ~payload-sized intermediate DOM (the element→node→T double round-trip).
        if (_source.TryGetElement(path, out var element))
        {
            // Present-but-null ≡ former GetAsNode null; intercept before Deserialize.
            if (element.ValueKind == JsonValueKind.Null) return default;
            return element.Deserialize<T>(SystemTextJsonOptions.Default);
        }
        // Node fall-through: overlay lifted (HasWrites) or child context — a node already exists,
        // so node→T is free (no double round-trip to eliminate).
        var node = GetAsNode(path);
        if (node is null) return default;
        return node.Deserialize<T>(SystemTextJsonOptions.Default);
    }

    /// <inheritdoc />
    public IEnumerable<T?>? GetArray<T>(string path)
    {
        if (_source.TryGetElement(path, out var element))
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    // Eager List: the borrowed element handle is valid only synchronously (it is a
                    // view over the live base document), so it must be fully consumed before this
                    // method returns. Do NOT relax to a lazy Select here (see the node branch).
                    var list = new List<T?>(element.GetArrayLength());
                    foreach (var item in element.EnumerateArray())
                        list.Add(item.ValueKind == JsonValueKind.Null
                            ? default
                            : item.Deserialize<T>(SystemTextJsonOptions.Default));
                    return list;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                    return null;
                default:
                    return new[] { element.Deserialize<T>(SystemTextJsonOptions.Default) }; // scalar → singleton
            }
        }
        // Node fall-through (overlay lifted / child): the node is heap-owned, so the lazy Select
        // is safe here.
        var node = GetAsNode(path);
        if (node is null) return null;
        if (node is JsonArray arr) return arr.Select(item => item is null ? default : item.Deserialize<T>(SystemTextJsonOptions.Default));
        if (node is JsonValue val) return new[] { val.Deserialize<T>(SystemTextJsonOptions.Default) };
        return null;
    }

    /// <inheritdoc />
    public object? GetValue(string path, bool parseDateStrings = true) =>
        _source.GetValue(path, parseDateStrings);

    /// <inheritdoc />
    public bool TryGet<T>(string path, out T? value)
    {
        if (!Exists(path)) { value = default; return false; }
        value = Get<T>(path);
        return true;
    }

    /// <inheritdoc />
    public void Set<T>(string path, T? value) =>
        Set(path, value, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Overwrite);

    /// <inheritdoc />
    public void Set<T>(string path,
        T? value,
        DocumentModes documentMode,
        ValueKinds valueKind,
        TargetValueWriteModes writeMode)
    {
        if (documentMode == DocumentModes.Replace)
        {
            _overlay.Write("$", new JsonObject());
        }

        // CLR values become part of the navigable document tree, so build them with the
        // case-sensitive node bundle (Newtonsoft JObject.FromObject parity); a JsonNode value keeps
        // its own options via DeepClone. Typed reads (Get<T>) stay on the case-insensitive Default.
        var node = value is null
            ? null
            : (value is JsonNode jn ? jn.DeepClone() : JsonSerializer.SerializeToNode(value, SystemTextJsonOptions.NodeNavigation));

        if (path == "$" || string.IsNullOrEmpty(path))
        {
            _overlay.Write("$", node);
            return;
        }

        // §5.1 invariant applies to ALL non-Replace, non-root writes — not just Overwrite.
        // Append/Prepend/Merge also lift the empty child base into a sparse overlay shape
        // that would otherwise shadow parent-fallback siblings at every intermediate
        // ancestor. Seed once before the switch; the function is idempotent. No-op on the
        // root source (ElementSource), so the root's full-base-lift behaviour is unchanged.
        _source.SeedAncestorsForWrite(path);

        switch (writeMode)
        {
            case TargetValueWriteModes.Overwrite:
                _overlay.Write(path, valueKind == ValueKinds.Array ? new JsonArray(node) : node);
                break;
            case TargetValueWriteModes.Append:
                AppendOrPrependCore(_overlay, GetAsNode, path, node, prepend: false);
                break;
            case TargetValueWriteModes.Prepend:
                AppendOrPrependCore(_overlay, GetAsNode, path, node, prepend: true);
                break;
            case TargetValueWriteModes.Merge:
                MergeAtCore(_overlay, GetAsNode, path, node);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(writeMode));
        }
    }

    /// <summary>
    /// Shared Append/Prepend write-mode logic. Reads existing state via
    /// <paramref name="readExisting"/> and writes the result into <paramref name="overlay"/>.
    /// </summary>
    private static void AppendOrPrependCore(DataOverlay overlay, Func<string, JsonNode?> readExisting,
        string path, JsonNode? node, bool prepend)
    {
        var existing = readExisting(path);
        if (existing is JsonArray arr)
        {
            var clone = arr.DeepClone()!.AsArray();
            if (node is JsonArray nodeArr)
            {
                foreach (var item in nodeArr.ToList())
                {
                    var cloned = item?.DeepClone();
                    if (prepend) clone.Insert(0, cloned); else clone.Add(cloned);
                }
            }
            else
            {
                if (prepend) clone.Insert(0, node); else clone.Add(node);
            }
            overlay.Write(path, clone);
        }
        else if (existing is null)
        {
            var newArr = new JsonArray();
            if (node is not null) newArr.Add(node);
            overlay.Write(path, newArr);
        }
        else
        {
            throw DataPipelineException.ValueIsArrayMustBeScalarForWriteMode(path,
                prepend ? TargetValueWriteModes.Prepend : TargetValueWriteModes.Append);
        }
    }

    /// <summary>
    /// Shared Merge write-mode logic. Reads existing state via <paramref name="readExisting"/>
    /// and writes the merged result into <paramref name="overlay"/>.
    /// </summary>
    private static void MergeAtCore(DataOverlay overlay, Func<string, JsonNode?> readExisting,
        string path, JsonNode? node)
    {
        var existing = readExisting(path);
        if (existing is JsonObject existingObj && node is JsonObject newObj)
        {
            var merged = (JsonObject)existingObj.DeepClone();
            foreach (var kvp in newObj)
            {
                merged[kvp.Key] = kvp.Value?.DeepClone();
            }
            overlay.Write(path, merged);
            return;
        }
        if (node is not JsonObject newObjFirstWrite)
        {
            throw DataPipelineException.SourceValueIsObjectMustBeObjectForWriteMode(path, TargetValueWriteModes.Merge);
        }
        if (existing is null)
        {
            // First-write semantics: merging an object into a missing path mirrors
            // Newtonsoft's lenient JObject.Merge — assign the new object as-is.
            overlay.Write(path, newObjFirstWrite.DeepClone());
            return;
        }
        throw DataPipelineException.TargetValueIsObjectMustBeObjectForWriteMode(path, TargetValueWriteModes.Merge);
    }

    /// <inheritdoc />
    public void Clear(string path) => _overlay.Clear(path);

    /// <inheritdoc />
    public Task IterateArrayAsync(string path, Func<IDataContext, Task> body)
    {
        var node = GetAsNode(path);
        if (node is not JsonArray arr) return Task.CompletedTask;
        return RunSequential(arr, body);
    }

    /// <inheritdoc />
    public async Task IterateArrayAsync(string path, IReadOnlyList<(string Alias, string SourcePath)> aliases,
        Func<IDataContext, Task> body)
    {
        var node = GetAsNode(path);
        if (node is not JsonArray arr) return;

        // Resolve each alias source path to a JsonElement once, before iteration begins.
        // This produces the (AliasPath, JsonElement) tuples expected by CreateIterationChild.
        var resolved = ResolveAliasElements(aliases);

        foreach (var item in arr)
        {
            // Create a per-item child via the iteration factory (which seeds $ with the
            // item) and run the body against it. CreateIterationChild deep-clones the item
            // and sets it as the child's root so item-relative paths (e.g. "$.foo") work.
            var sub = ((IIterationContextFactory)this).CreateIterationChild(resolved, item);
            await body(sub).ConfigureAwait(false);
        }
    }

    IReadOnlyList<(string AliasPath, JsonElement Value)> IIterationContextFactory.ResolveAliasElements(
        IReadOnlyList<(string Alias, string SourcePath)> aliases) => ResolveAliasElements(aliases);

    IDataContext IIterationContextFactory.CreateIterationChild(
        IReadOnlyList<(string AliasPath, JsonElement Value)> aliases, JsonNode? item)
    {
        var sub = new DataContextImpl(this, aliases);
        sub.SetIterationRoot(item is null ? null : item.DeepClone());
        return sub;
    }

    private List<(string AliasPath, JsonElement Value)> ResolveAliasElements(
        IReadOnlyList<(string Alias, string SourcePath)> aliases)
    {
        var resolved = new List<(string AliasPath, JsonElement Value)>(aliases.Count);
        foreach (var (alias, sourcePath) in aliases)
        {
            // Fall back to the whole effective document ($) when the source path does not
            // resolve, so aliases over the full document keep working. On the root this is
            // the base/lifted root; on a child it is the layered ($-view) effective root.
            // GetEffectiveNode (not GetAsNode/TryGetNode) folds the source's own aliases into a
            // "$" snapshot, so a child's "$.full" carries forward into a nested child — without
            // it, "$.full.full" (grandparent) reads as Undefined in a ForEach-inside-a-ForEach.
            var src = _source.GetEffectiveNode(sourcePath) ?? _source.GetEffectiveNode("$");
            // We need a JsonElement that outlives the iteration. SerializeToElement yields an
            // owned element (survives) without the ToJsonString -> Parse -> Clone UTF-16 round-trip;
            // a null source serialises to a JSON-null element, matching the former "null" fallback.
            resolved.Add((alias, JsonSerializer.SerializeToElement(src, SystemTextJsonOptions.Default)));
        }
        return resolved;
    }

    /// <summary>
    /// Seeds the child's overlay root with the current iteration item so the body of
    /// <see cref="IterateArrayAsync(string, IReadOnlyList{ValueTuple{string, string}}, Func{IDataContext, Task})"/>
    /// can read item-relative paths via the regular <see cref="IDataContext"/> API.
    /// No-op meaning on a root context; only ever called on freshly-built iteration children.
    /// </summary>
    private void SetIterationRoot(JsonNode? item)
    {
        _overlay.Write("$", item);
    }

    private static async Task RunSequential(JsonArray arr, Func<IDataContext, Task> body)
    {
        foreach (var item in arr)
        {
            // Detach each array item into a standalone node-backed child via DeepClone (isolated
            // from the source array) — no ToJsonString/Parse round-trip.
            using var child = new DataContextImpl(item?.DeepClone());
            await body(child);
        }
    }

    /// <inheritdoc />
    public async Task IterateObjectAsync(string path, Func<string, IDataContext, Task> body)
    {
        var node = GetAsNode(path);
        if (node is not JsonObject obj) return;
        foreach (var kvp in obj)
        {
            // Detach each property value into a standalone node-backed child via DeepClone
            // (isolated from the source object) — no ToJsonString/Parse round-trip.
            using var child = new DataContextImpl(kvp.Value?.DeepClone());
            await body(kvp.Key, child);
        }
    }

    /// <inheritdoc />
    public async Task IterateMatchesAsync(string jsonPath, Func<IDataContext, Task> body)
    {
        // Each Evaluate match arrives already detached (JsonElement.Clone / JsonNode.DeepClone) —
        // no per-match serialize/parse round-trip. Wrap it in a standalone child for the body.
        foreach (var match in _source.Evaluate(jsonPath))
        {
            using var child = FromMatch(match);
            await body(child);
        }
    }

    /// <inheritdoc />
    public async Task UpdateMatchesAsync(string jsonPath, Func<IDataContext, Task> body)
    {
        // Matches arrive detached (no per-match round-trip) and canonical paths are captured up
        // front, so we are not iterating while mutating the overlay. Unlike SelectMatches, the
        // body's sub-context is parented to THIS context (CreateIterationChild) so transform-node
        // bodies that read outside the matched subtree resolve via parent fallback.
        var matches = _source.Evaluate(jsonPath);

        foreach (var match in matches)
        {
            var matchNode = match.IsNode ? match.Node : JsonDetach.ToNode(match.Element!.Value);
            var subCtx = ((IIterationContextFactory)this).CreateIterationChild(
                Array.Empty<(string AliasPath, JsonElement Value)>(), matchNode);
            await body(subCtx).ConfigureAwait(false);
            var resultNode = subCtx.Get<JsonNode>("$");
            // Write back via Set (not raw overlay) so the child seeds parent-fallback
            // siblings at intermediate ancestors. On the root SeedAncestorsForWrite is a
            // no-op, so this is equivalent to the former raw overlay write there.
            Set(match.CanonicalPath, resultNode);
        }
    }

    /// <inheritdoc />
    public IDataContext? Select(string path)
    {
        var node = GetAsNode(path);
        if (node is null) return null;
        // Detach via DeepClone into a standalone node-backed context — no ToJsonString/Parse hop.
        return new DataContextImpl(node.DeepClone());
    }

    /// <inheritdoc />
    public IEnumerable<IDataContext> SelectMatches(string jsonPath)
    {
        var results = new List<IDataContext>();
        // Matches arrive detached (Clone/DeepClone); wrap each as a standalone (un-parented)
        // sub-context that survives this context's disposal.
        foreach (var match in _source.Evaluate(jsonPath))
            results.Add(FromMatch(match));
        return results;
    }

    /// <summary>
    /// Wraps a detached <see cref="DetachedMatch"/> as a standalone sub-context: node-backed for
    /// node/lifted matches (and JSON null), element-backed for the zero-copy element fast path.
    /// </summary>
    private static DataContextImpl FromMatch(in DetachedMatch match) =>
        match.IsNode
            ? new DataContextImpl(match.Node)
            : new DataContextImpl(match.Element!.Value);

    /// <inheritdoc />
    public void CopyTo(string sourcePath, string targetPath)
    {
        var src = GetAsNode(sourcePath);
        if (src is not null) _overlay.Write(targetPath, src.DeepClone());
    }

    /// <inheritdoc />
    public void WriteJsonTo(string path, Stream destination)
    {
        var node = GetAsNode(path);
        if (node is null) return;
        // Honour the SDK's configured encoder (UnsafeRelaxedJsonEscaping) so serialized output matches
        // Newtonsoft byte-for-byte on non-ASCII (umlauts/ß) and HTML chars (< > &). A bare Utf8JsonWriter
        // uses STJ's default encoder, which \uXXXX-escapes those — diverging from the legacy wire form
        // and breaking hash/HMAC parity. Single source of truth: SystemTextJsonOptions.Default.Encoder.
        using var writer = new Utf8JsonWriter(destination,
            new JsonWriterOptions { Encoder = SystemTextJsonOptions.Default.Encoder });
        node.WriteTo(writer);
    }

    /// <inheritdoc />
    public void SetFromJson(string path, ReadOnlyMemory<byte> utf8Json)
    {
        var node = JsonNode.Parse(utf8Json.Span);
        _overlay.Write(path, node);
    }

    private JsonNode? GetAsNode(string path) => _source.TryGetNode(path);

    /// <summary>
    /// Creates a child context for zero-copy iteration scenarios. The child reads from
    /// alias entries first, then falls back to the parent for everything else. Writes on
    /// the child stay isolated in the child's overlay and do not escape to the parent.
    /// </summary>
    internal IDataContext CreateIterationChild(IReadOnlyList<(string AliasPath, JsonElement Value)> aliases)
    {
        return new DataContextImpl(this, aliases);
    }

    /// <summary>
    /// Test-only observability hook: reports whether the underlying overlay has lifted
    /// state or tombstones. Used by orchestrator tests to assert that an empty pipeline
    /// run does not accidentally lift the base document into the overlay (spec §5.1).
    /// </summary>
    internal bool OverlayHasWrites => _overlay.HasWrites;

    // IDataContextFallbackSource — lets a downstream child read this context's lookup
    // chain as its parent. Both members route through the read seam, so the body is
    // identical for a root context (ElementSource) and a child (LayeredSource):
    // on the root, TryGetNode is `_overlay.TryRead(...) ? n : null` and IsPathTombstoned
    // is the overlay's own tombstone state; on a child, both are the full layered
    // overlay → aliases → parent-fallback chain (so the L3 nested-iteration chain,
    // which passes `this` as the next child's parent, is unchanged).
    JsonNode? IDataContextFallbackSource.TryGetNodeForChildFallback(string path) =>
        _source.TryGetNode(path);

    bool IDataContextFallbackSource.IsPathTombstoned(string path) => _source.IsPathTombstoned(path);

    // IDebugSnapshotSource — folds the source's synthetic top-level aliases (e.g. an iteration
    // child's "$.full") into the "$" snapshot, which plain Get<JsonNode>("$")/TryGetNode omit.
    // No defensive DeepClone: on an iteration child GetEffectiveNode already returns an owned
    // clone, and the debugger serialises the node synchronously and read-only at capture time
    // (see DefaultPipelineDebugger.SerializeSnapshot), so a shared root node is consumed before
    // any later mutation. This whole call is reached only via PipelineDebugger?. — when debugging
    // is off it is never evaluated, so it allocates nothing on the non-debug pipeline path.
    JsonNode? IDebugSnapshotSource.GetDebugSnapshot() => _source.GetEffectiveNode("$");

    /// <summary>
    /// Releases the <see cref="JsonDocument"/> owned by this context, if any. Idempotent.
    /// Constructors that take an externally-owned <see cref="JsonElement"/>, and the
    /// iteration-child constructor, hold no document — Dispose is a no-op for those.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownedDocument?.Dispose();
    }
}
