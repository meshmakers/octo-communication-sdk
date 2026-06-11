using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

internal sealed class DataOverlay
{
    private readonly JsonElement _base;
    // When _isLifted is true, all reads/writes route through _lifted instead of _base.
    // _lifted itself may be null — that represents an explicit "the overlay's root is
    // the JSON value null", which is a real, observable state distinct from "no writes
    // have happened yet". Without the explicit _isLifted flag, Write("$", null) would be
    // indistinguishable from the unwritten initial state and TryRead would silently fall
    // through to the base.
    private bool _isLifted;
    private JsonNode? _lifted;
    // Tombstones mark paths that the caller explicitly cleared. They make Clear
    // authoritative on top of any base/parent fallback: a tombstoned path reports
    // as absent (Exists/PathExists/TryRead all return absent) until a subsequent
    // Write at that exact path lifts the tombstone. Used primarily by the child
    // context so a child's Clear hides a parent-fallback value from that child.
    private readonly HashSet<string> _tombstones = new(StringComparer.Ordinal);

    public DataOverlay(JsonElement baseElement)
    {
        _base = baseElement;
    }

    public bool HasWrites => _isLifted || _tombstones.Count > 0;

    /// <summary>
    /// Returns true if the given path was explicitly cleared on this overlay and
    /// has not been re-written since. Callers that layer this overlay over another
    /// data source (e.g. <c>LayeredSource</c>) consult this to decide whether
    /// to fall through on a miss; a tombstoned miss is authoritative and must NOT
    /// fall through.
    /// </summary>
    public bool IsTombstoned(string canonicalPath) => _tombstones.Contains(canonicalPath);

    public bool TryRead(string canonicalPath, out JsonNode? value)
    {
        if (_tombstones.Contains(canonicalPath))
        {
            // Tombstoned paths are authoritatively absent on this overlay.
            value = null;
            return false;
        }

        if (_isLifted)
        {
            // _lifted can be null here (explicit Write("$", null)). The root path
            // returns it directly; nested paths can't resolve through a null root,
            // so they report absent.
            if (_lifted is null)
            {
                value = null;
                return canonicalPath == "$";
            }
            value = NavigateLifted(_lifted, canonicalPath, allowMissing: true);
            // Use PathExistsInLifted (which checks object-key/array-slot existence)
            // so an explicitly-written null value is reported as present (returns true
            // with value == null). PathExistsLifted (NavigateLifted-based) collapses
            // missing and explicit-null into the same not-found result.
            return value is not null || PathExistsInLifted(_lifted, canonicalPath);
        }

        // No writes yet — walk the base.
        var matches = JsonPathWalker.Select(new ElementView(_base), canonicalPath).ToList();
        if (matches.Count == 0)
        {
            value = null;
            return false;
        }
        // Materialize the JsonElement match as a JsonNode (copy at read time) WITHOUT a UTF-16
        // string round-trip. For explicit JSON null in the base, ToNode returns null — exactly
        // the "present but null" representation we want.
        value = JsonDetach.ToNode(matches[0].Match.Element);
        return true;
    }

    /// <summary>
    /// Returns true if the path exists somewhere — when the overlay has been lifted,
    /// only the lifted state is considered authoritative; otherwise the base document
    /// is consulted. This semantic differs from <see cref="TryRead"/> by intentionally
    /// not falling through to the base after the lifted state has diverged.
    /// </summary>
    public bool PathExists(string canonicalPath)
    {
        if (_tombstones.Contains(canonicalPath)) return false;

        if (_isLifted)
        {
            // Same null-root handling as TryRead: a null lifted root means the root
            // path exists (it's the JSON value null) but no nested path does.
            if (_lifted is null) return canonicalPath == "$";
            return PathExistsInLifted(_lifted, canonicalPath);
        }

        // No writes yet — walk the base.
        return JsonPathWalker.Select(new ElementView(_base), canonicalPath).Any();
    }

    private static bool PathExistsInLifted(JsonNode root, string canonicalPath)
    {
        if (canonicalPath == "$") return true;
        var segments = CanonicalPath.GetSegments(canonicalPath);
        JsonNode? current = root;
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var isLast = i == segments.Count - 1;
            if (current is null) return false;
            if (seg.StartsWith("."))
            {
                var name = seg.Substring(1);
                if (current is JsonObject obj && obj.ContainsKey(name))
                {
                    if (isLast) return true;
                    current = obj[name];
                }
                else if (IsStringIdVirtualProperty(current, name))
                {
                    if (isLast) return true;
                    // Virtual property aliases the string to itself; further drilling is impossible.
                    return false;
                }
                else
                {
                    return false;
                }
            }
            else if (seg.StartsWith("[") && seg.EndsWith("]"))
            {
                var idxStr = seg.Substring(1, seg.Length - 2);
                if (!int.TryParse(idxStr, out var idx)) return false;
                if (current is not JsonArray arr || idx < 0 || idx >= arr.Count) return false;
                if (isLast) return true;
                current = arr[idx];
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    public void Write(string canonicalPath, JsonNode? value)
    {
        // Writing to a path lifts any prior tombstone — the new value is authoritative.
        _tombstones.Remove(canonicalPath);

        if (canonicalPath == "$")
        {
            // #13: skip EnsureLifted's full base copy when we're about to overwrite
            // the root anyway. Also covers the explicit-null root write case (#2) —
            // _isLifted records the write, _lifted stores the (possibly null) value.
            _lifted = value;
            _isLifted = true;
            return;
        }

        EnsureLifted();
        // After an explicit Write("$", null), _lifted is null but a subsequent nested
        // write needs structure to walk into. Promote to an empty object — the previous
        // null-root state is preserved everywhere except at the path being written.
        // Re-parsing the base is wrong: the null-root write was an authoritative reset,
        // so base subtrees must not become visible again.
        if (_lifted is null) _lifted = new JsonObject();
        var segments = CanonicalPath.GetSegments(canonicalPath);
        SetByPath(_lifted, segments, value);
    }

    public void Clear(string canonicalPath)
    {
        if (canonicalPath == "$")
        {
            _lifted = null;
            _isLifted = false;
            _tombstones.Clear();
            return;
        }
        // Record the clear as a tombstone so this overlay reports the path as
        // authoritatively absent even when no prior Write existed in the lifted
        // state — e.g. a child context clearing a path that only lives in the
        // parent fallback.
        _tombstones.Add(canonicalPath);
        if (!_isLifted || _lifted is null) return;
        var segments = CanonicalPath.GetSegments(canonicalPath);
        ClearByPath(_lifted, segments);
    }

    private void EnsureLifted()
    {
        if (!_isLifted)
        {
            // Lift the immutable base into a mutable node tree without a UTF-16 string round-trip.
            _lifted = JsonDetach.ToNode(_base);
            _isLifted = true;
        }
    }

    private static JsonNode? NavigateLifted(JsonNode root, string canonicalPath, bool allowMissing)
    {
        if (canonicalPath == "$") return root;
        var segments = CanonicalPath.GetSegments(canonicalPath);
        JsonNode? current = root;
        foreach (var seg in segments)
        {
            current = StepInto(current, seg);
            if (current is null) return allowMissing ? null : throw new InvalidOperationException($"Path not found: {canonicalPath}");
        }
        return current;
    }

    private static bool PathExistsLifted(JsonNode root, string canonicalPath) =>
        NavigateLifted(root, canonicalPath, allowMissing: true) is not null;

    private static JsonNode? StepInto(JsonNode? current, string segment)
    {
        if (current is null) return null;
        if (segment.StartsWith("."))
        {
            var name = segment.Substring(1);
            if (current is JsonObject obj && obj.TryGetPropertyValue(name, out var child)) return child;
            if (IsStringIdVirtualProperty(current, name)) return current;
            return null;
        }
        if (segment.StartsWith("[") && segment.EndsWith("]"))
        {
            var idxStr = segment.Substring(1, segment.Length - 2);
            if (int.TryParse(idxStr, out var idx) && current is JsonArray arr && idx >= 0 && idx < arr.Count)
            {
                return arr[idx];
            }
            return null;
        }
        throw new ArgumentException($"Unknown segment: {segment}");
    }

    /// <summary>
    /// CK identifier compatibility: <see cref="Meshmakers.Octo.ConstructionKit.Contracts.RtCkId{T}"/>
    /// is serialized as a JSON string (its <c>SemanticVersionedFullName</c>) by
    /// <c>RtCkIdConverter</c>. Legacy pipeline YAML drills into <c>.SemanticVersionedFullName</c> or
    /// <c>.FullName</c> expecting the historical reflection-emitted object shape. Treating those two
    /// property names as virtual self-aliases on a string node lets those paths resolve without
    /// inflating the wire format. Mirrors the same shim in <c>JsonPathWalker.SelectProperty</c>.
    /// </summary>
    private static bool IsStringIdVirtualProperty(JsonNode current, string propertyName)
    {
        return RtCkIdJsonShim.IsVirtualProperty(propertyName)
               && current is JsonValue jv
               && jv.GetValueKind() == JsonValueKind.String;
    }

    private static void SetByPath(JsonNode root, IReadOnlyList<string> segments, JsonNode? value)
    {
        JsonNode current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            current = NavigateOrCreate(current, segments[i]);
        }
        SetSegment(current, segments[segments.Count - 1], value);
    }

    private static JsonNode NavigateOrCreate(JsonNode current, string segment)
    {
        if (segment.StartsWith("."))
        {
            var name = segment.Substring(1);
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(name, out var child) || child is null)
                {
                    child = new JsonObject();
                    obj[name] = child;
                }
                return child;
            }
            throw new InvalidOperationException($"Cannot navigate '{segment}' on non-object");
        }
        if (segment.StartsWith("["))
        {
            var idx = int.Parse(segment.Substring(1, segment.Length - 2));
            if (current is JsonArray arr)
            {
                while (arr.Count <= idx) arr.Add(null);
                if (arr[idx] is null) arr[idx] = new JsonObject();
                return arr[idx]!;
            }
            throw new InvalidOperationException($"Cannot navigate '{segment}' on non-array");
        }
        throw new ArgumentException($"Unknown segment: {segment}");
    }

    private static void SetSegment(JsonNode current, string segment, JsonNode? value)
    {
        if (segment.StartsWith("."))
        {
            var name = segment.Substring(1);
            if (current is JsonObject obj) { obj[name] = value; return; }
            throw new InvalidOperationException($"Cannot set '{segment}' on non-object");
        }
        if (segment.StartsWith("["))
        {
            var idx = int.Parse(segment.Substring(1, segment.Length - 2));
            if (current is JsonArray arr)
            {
                while (arr.Count <= idx) arr.Add(null);
                arr[idx] = value;
                return;
            }
            throw new InvalidOperationException($"Cannot set '{segment}' on non-array");
        }
        throw new ArgumentException($"Unknown segment: {segment}");
    }

    private static void ClearByPath(JsonNode root, IReadOnlyList<string> segments)
    {
        JsonNode? current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            current = StepInto(current, segments[i]);
            if (current is null) return;
        }
        var last = segments[segments.Count - 1];
        if (last.StartsWith(".") && current is JsonObject obj) obj.Remove(last.Substring(1));
        else if (last.StartsWith("[") && current is JsonArray arr)
        {
            var idx = int.Parse(last.Substring(1, last.Length - 2));
            if (idx >= 0 && idx < arr.Count) arr.RemoveAt(idx);
        }
    }
}
