using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Reads and mutates an in-memory <see cref="JsonNode"/> tree by JSONPath, and normalizes
/// user-supplied path strings to a canonical rooted form. Writes (Set/Remove) are
/// dotted-only by design — multi-match writes use <c>IDataContext.UpdateMatchesAsync</c>
/// instead. The read members (<see cref="Select"/>/<see cref="SelectAll"/>) are thin public
/// entry points over the single generic <see cref="JsonPathWalker"/> (no second walker); they
/// exist for callers that already hold a raw <see cref="JsonNode"/> subtree (e.g. adapter nodes
/// doing structural grouping/sorting) rather than an <c>IDataContext</c>. They return the
/// matched node <em>by reference</em> into the root subtree — no clone, no serialize.
/// </summary>
public static class JsonNodePath
{
    /// <summary>
    /// The first node matching <paramref name="path"/> on <paramref name="root"/>, or <c>null</c>
    /// when no match exists or <paramref name="root"/> is <c>null</c>. Walks by reference via
    /// <see cref="JsonPathWalker"/> — the returned node is a live reference into <paramref name="root"/>.
    /// </summary>
    public static JsonNode? Select(JsonNode? root, string path) => SelectAll(root, path).FirstOrDefault();

    /// <summary>
    /// All nodes matching <paramref name="path"/> on <paramref name="root"/> in document order
    /// (empty when no match or <paramref name="root"/> is <c>null</c>). Full JSONPath dialect via
    /// <see cref="JsonPathWalker"/>; each yielded node is a live reference into <paramref name="root"/>.
    /// </summary>
    public static IEnumerable<JsonNode?> SelectAll(JsonNode? root, string path)
    {
        if (root is null) yield break;
        foreach (var (match, _) in JsonPathWalker.Select(new NodeView(root), path))
            yield return match.Node;
    }

    /// <summary>
    /// Sets the value at <paramref name="path"/> on <paramref name="root"/>, creating
    /// intermediate <see cref="JsonObject"/> nodes for any missing dotted segments.
    /// </summary>
    /// <param name="root">Root object to mutate.</param>
    /// <param name="path">JSONPath expression. Must be a dotted property path (no indices, wildcards, filters, or recursive descent).</param>
    /// <param name="value">Value to assign at the path.</param>
    /// <exception cref="JsonPathNotSupportedException">
    /// Thrown if the path contains an indexed segment, wildcard, filter, or recursive descent,
    /// or if an intermediate node along the path exists but is not a <see cref="JsonObject"/>.
    /// </exception>
    public static void Set(JsonNode root, string path, JsonNode? value)
    {
        var segments = ParseDottedSegments(path);
        if (segments.Count == 0)
        {
            // No property segments after $ — nothing to set.
            return;
        }

        if (root is not JsonObject rootObj)
        {
            throw new JsonPathNotSupportedException("set on non-object root", path, 0);
        }

        var current = rootObj;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var segName = segments[i];
            if (current.TryGetPropertyValue(segName, out var next))
            {
                if (next is JsonObject existing)
                {
                    current = existing;
                }
                else
                {
                    throw new JsonPathNotSupportedException(
                        $"cannot navigate into non-object intermediate '{segName}'", path, 0);
                }
            }
            else
            {
                var fresh = new JsonObject();
                current[segName] = fresh;
                current = fresh;
            }
        }

        var finalKey = segments[segments.Count - 1];
        current[finalKey] = value?.DeepClone();
    }

    /// <summary>
    /// Removes the property at <paramref name="path"/>. Returns true if a property was removed.
    /// </summary>
    /// <param name="root">Root object to mutate.</param>
    /// <param name="path">JSONPath expression. Must be a dotted property path (no indices, wildcards, filters, or recursive descent).</param>
    /// <returns>True if a property was removed; false if the path did not resolve to an existing property.</returns>
    /// <exception cref="JsonPathNotSupportedException">
    /// Thrown if the path contains an indexed segment, wildcard, filter, or recursive descent.
    /// </exception>
    public static bool Remove(JsonNode root, string path)
    {
        var segments = ParseDottedSegments(path);
        if (segments.Count == 0)
        {
            return false;
        }

        if (root is not JsonObject rootObj)
        {
            return false;
        }

        var current = rootObj;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (!current.TryGetPropertyValue(segments[i], out var next) || next is not JsonObject existing)
            {
                return false;
            }
            current = existing;
        }

        return current.Remove(segments[segments.Count - 1]);
    }

    /// <summary>
    /// Parses the path and returns the list of property names, validating that every
    /// segment is a <see cref="PropertySegment"/>. Throws <see cref="JsonPathNotSupportedException"/>
    /// for any indexed, wildcard, filter, or recursive descent segment.
    /// </summary>
    private static List<string> ParseDottedSegments(string path)
    {
        var expression = JsonPathParser.Parse(NormalizePathOrRelative(path));
        var segments = new List<string>();
        foreach (var seg in expression.Segments)
        {
            switch (seg)
            {
                case RootSegment:
                    continue;
                case PropertySegment p:
                    segments.Add(p.Name);
                    break;
                case IndexSegment:
                    throw new JsonPathNotSupportedException("indexed segment in write path", path, 0);
                case WildcardSegment:
                    throw new JsonPathNotSupportedException("wildcard in write path", path, 0);
                case FilterSegment:
                    throw new JsonPathNotSupportedException("filter in write path", path, 0);
                case RecursiveDescentSegment:
                    throw new JsonPathNotSupportedException("recursive descent in write path", path, 0);
                default:
                    throw new JsonPathNotSupportedException($"segment {seg.GetType().Name} in write path", path, 0);
            }
        }
        return segments;
    }

    /// <summary>
    /// Normalizes user-supplied JSONPath strings — bare ("id"), leading-dot (".id"),
    /// rooted ("$.id"), and bracket-rooted ("$['foo-bar']") all resolve to a canonical
    /// rooted form. Replaces the per-node <c>NormalizeRelative</c> duplicates.
    /// </summary>
    public static string NormalizePathOrRelative(string path)
    {
        if (string.IsNullOrEmpty(path)) return "$";
        if (path == "$") return path;
        if (path.StartsWith("$.", StringComparison.Ordinal)) return path;
        if (path.StartsWith("$[", StringComparison.Ordinal)) return path;
        if (path.StartsWith(".", StringComparison.Ordinal)) return "$" + path;
        return "$." + path;
    }
}
