using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Read backend for a <see cref="DataContextImpl"/>. The seam encapsulates the
/// read representation behind the path-only API: <c>ElementSource</c> serves a
/// zero-copy <see cref="System.Text.Json.JsonElement"/> base (the root context);
/// a later <c>LayeredSource</c> will serve aliases + parent-fallback + overlay
/// (iteration children).
/// </summary>
/// <remarks>
/// Each source owns its own overlay and therefore its own <c>_overlay.HasWrites</c>
/// fast-path discriminator — the discriminator never crosses this seam, so it is not
/// a member here. All single-path reads (<see cref="PathExists"/>, <see cref="TryGetNode"/>,
/// <see cref="GetKind"/>, <see cref="GetValue"/>) and the multi-match
/// <see cref="Evaluate"/> are resolved entirely inside the source.
/// </remarks>
internal interface IReadSource
{
    /// <summary>
    /// Authoritative existence check for <paramref name="path"/>. Reports an
    /// explicitly-written null as present (mirrors <c>DataOverlay.PathExists</c>),
    /// distinct from a missing path.
    /// </summary>
    bool PathExists(string path);

    /// <summary>
    /// Resolves a single path to a node (overlay-lifted-or-base), or <c>null</c>
    /// when absent or explicitly null. Backs <c>Get&lt;T&gt;</c>/<c>GetArray</c>/
    /// <c>Length</c>/<c>Keys</c>/<c>CopyTo</c>/<c>WriteJsonTo</c>.
    /// </summary>
    JsonNode? TryGetNode(string path);

    /// <summary>
    /// Resolves <paramref name="path"/> to its FULL effective node, folding any alias entries
    /// the source layers on top of its base into the result. Unlike <see cref="TryGetNode"/> —
    /// where a read of <c>"$"</c> on an iteration child returns only the overlay and omits the
    /// child's synthetic top-level aliases (e.g. <c>"$.full"</c>, which is not an ancestor of
    /// <c>"$"</c>) — this includes them. Backs the alias-source snapshot in
    /// <c>DataContextImpl.ResolveAliasElements</c>: a snapshot taken over a child's <c>"$"</c>
    /// must carry that child's aliases forward so a NESTED iteration child can still reach the
    /// grandparent document via <c>"$.full.full"</c>. On the root source (no aliases) this
    /// equals <see cref="TryGetNode"/>.
    /// </summary>
    JsonNode? GetEffectiveNode(string path);

    /// <summary>Returns the <see cref="DataKind"/> classification of the value at <paramref name="path"/>.</summary>
    DataKind GetKind(string path);

    /// <summary>
    /// Reads <paramref name="path"/> as its natural CLR scalar via the shared
    /// JsonScalar rules; object/array kinds return <c>null</c>.
    /// </summary>
    object? GetValue(string path, bool parseDateStrings);

    /// <summary>
    /// Zero-copy typed-read seam. When this source can resolve <paramref name="path"/> directly
    /// against an immutable <see cref="JsonElement"/> base WITHOUT materialising a
    /// <see cref="JsonNode"/>, sets <paramref name="element"/> to that base element (a struct
    /// handle over the live base document — NOT cloned; valid only for synchronous consumption
    /// inside the calling <c>Get&lt;T&gt;</c>/<c>GetArray&lt;T&gt;</c>) and returns <c>true</c>.
    /// Returns <c>false</c> when no element base serves this path (overlay lifted, or a child's
    /// alias/parent-fallback chain) — the caller then falls back to <see cref="TryGetNode"/>.
    /// A JSON-null element is reported as a successful match (kind <c>Null</c>) so the caller
    /// preserves present-but-null semantics. Lets a typed read deserialize straight off the base,
    /// skipping the element→node→T double round-trip.
    /// </summary>
    bool TryGetElement(string path, out JsonElement element);

    /// <summary>
    /// Multi-match evaluation: yields each match already <see cref="DetachedMatch">detached</see>
    /// from the source (via <see cref="JsonElement.Clone"/> / <see cref="JsonNode.DeepClone"/>) so
    /// it survives source/parent disposal — no per-match serialize/parse round-trip. Backs
    /// <c>SelectMatches</c>, <c>IterateMatchesAsync</c>, and <c>UpdateMatchesAsync</c> (the last
    /// consumes <see cref="DetachedMatch.CanonicalPath"/> for write-back).
    /// </summary>
    IEnumerable<DetachedMatch> Evaluate(string jsonPath);

    /// <summary>
    /// §5.1 ancestor seeding before a non-root, non-Replace write. Each source seeds
    /// its own overlay (the same instance it was constructed with). No-op on
    /// <c>ElementSource</c> (the root overlay lifts a full base copy, so siblings
    /// already exist); the real seeding lives on <c>LayeredSource</c>.
    /// </summary>
    void SeedAncestorsForWrite(string path);

    /// <summary>
    /// True if this source authoritatively reports <paramref name="path"/> as cleared.
    /// Backs the context's <see cref="IDataContextFallbackSource.IsPathTombstoned"/>.
    /// On <c>ElementSource</c> (root) this is simply the overlay's own tombstone state
    /// — the root has no parent fallback to consult; on <c>LayeredSource</c> it is the
    /// full layered tombstone view (own overlay plus ancestor tombstones via the seam).
    /// </summary>
    bool IsPathTombstoned(string path);
}
