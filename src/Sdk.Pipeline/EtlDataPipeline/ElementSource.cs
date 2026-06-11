using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// <see cref="IReadSource"/> over the root context's zero-copy
/// <see cref="JsonElement"/> base layered with its <see cref="DataOverlay"/>.
/// When the overlay has no writes, reads walk the immutable <see cref="JsonElement"/>
/// directly (no whole-document materialization); after the overlay lifts, reads
/// route through the lifted <see cref="JsonNode"/>. Read bodies are moved verbatim
/// from the former root <c>DataContextImpl</c> members.
/// </summary>
internal sealed class ElementSource : IReadSource
{
    private readonly JsonElement _base;
    private readonly DataOverlay _overlay;

    public ElementSource(JsonElement baseElement, DataOverlay overlay)
    {
        _base = baseElement;
        _overlay = overlay;
    }

    public bool PathExists(string path)
    {
        // Authoritative: once the overlay is lifted, only the lifted state determines
        // existence. Falling through to the base document would misreport a path as
        // present after callers deleted it from the lifted view.
        return _overlay.PathExists(path);
    }

    public JsonNode? TryGetNode(string path)
    {
        return _overlay.TryRead(path, out var node) ? node : null;
    }

    // The root source layers no aliases over its base, so the full effective node is just the
    // overlay-or-base resolution.
    public JsonNode? GetEffectiveNode(string path) => TryGetNode(path);

    public DataKind GetKind(string path)
    {
        if (_overlay.HasWrites)
        {
            if (!_overlay.TryRead(path, out var node)) return DataKind.Undefined;
            // node is null here can mean the path was explicitly written as null —
            // distinguish that from "missing" by reporting Null when TryRead returned true.
            if (node is null) return DataKind.Null;
            return DataKindMapper.KindOf(node);
        }

        var (match, canonicalPath) = JsonPathWalker.Select(new ElementView(_base), path).FirstOrDefault();
        if (canonicalPath is null) return DataKind.Undefined;
        return DataKindMapper.KindOf(match.Element);
    }

    public object? GetValue(string path, bool parseDateStrings)
    {
        if (!_overlay.HasWrites)
        {
            var (match, canonicalPath) = JsonPathWalker.Select(new ElementView(_base), path).FirstOrDefault();
            return canonicalPath is null ? null : JsonScalar.ToClr(match.Element, parseDateStrings);
        }
        var node = TryGetNode(path);
        return node is JsonValue jv ? JsonScalar.ToClr(jv, parseDateStrings) : null;
    }

    public bool TryGetElement(string path, out JsonElement element)
    {
        // Only the immutable base is element-addressable. Once the overlay lifts (HasWrites), the
        // authoritative state lives in the lifted JsonNode tree — defer to the node path, mirroring
        // the discriminator in GetValue and GetKind.
        if (_overlay.HasWrites)
        {
            element = default;
            return false;
        }

        var (match, canonicalPath) = JsonPathWalker.Select(new ElementView(_base), path).FirstOrDefault();
        if (canonicalPath is null)
        {
            element = default; // absent
            return false;
        }
        element = match.Element; // zero-copy struct handle over _base (incl. a present JSON null)
        return true;
    }

    public IEnumerable<DetachedMatch> Evaluate(string jsonPath)
    {
        var results = new List<DetachedMatch>();
        if (!_overlay.HasWrites)
        {
            // Zero-copy fast path: walk the immutable base; detach each match via Clone() (owns its
            // own buffer, survives this source's document) — no per-match string round-trip.
            foreach (var (match, canonicalPath) in JsonPathWalker.Select(new ElementView(_base), jsonPath))
                results.Add(JsonDetach.Detach(match, canonicalPath));
            return results;
        }
        // Lifted path: the overlay has writes. Walk the lifted overlay node DIRECTLY via NodeView
        // (parity-equivalent to the JsonElement walk, same as LayeredSource.Evaluate) instead of
        // serialising the whole lifted document to a string and re-parsing a snapshot — that
        // whole-document ToJsonString was the single biggest allocator (~65 MB/run) on the
        // read-heavy EDA pipeline. Detach each match via DeepClone (NodeView overload).
        var lifted = TryGetNode("$");
        if (lifted is null)
        {
            // HasWrites but "$" not lifted (defensive): fall back to the immutable base.
            foreach (var (match, canonicalPath) in JsonPathWalker.Select(new ElementView(_base), jsonPath))
                results.Add(JsonDetach.Detach(match, canonicalPath));
            return results;
        }
        foreach (var (match, canonicalPath) in JsonPathWalker.Select(new NodeView(lifted), jsonPath))
            results.Add(JsonDetach.Detach(match, canonicalPath));
        return results;
    }

    // The root overlay lifts a full base copy on the first nested write, so all
    // sibling keys already exist — no ancestor seeding is required.
    public void SeedAncestorsForWrite(string path)
    {
    }

    // The root has no parent fallback to consult, so a tombstone is reported
    // directly from this context's overlay (matches the former root inline body).
    public bool IsPathTombstoned(string path) => _overlay.IsTombstoned(path);
}
