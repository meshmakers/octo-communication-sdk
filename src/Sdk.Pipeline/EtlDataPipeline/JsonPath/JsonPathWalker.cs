using System.Collections.Generic;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Generic JSONPath walker over the <see cref="IJsonView{TSelf}"/> abstraction. The single read
/// path for the whole ETL pipeline: it walks either an <see cref="ElementView"/> (over
/// <see cref="JsonElement"/>) or a <see cref="NodeView"/> (over <see cref="System.Text.Json.Nodes.JsonNode"/>)
/// and produces, for any path, the matches AND the canonical path of each match. Cross-view parity
/// (ElementView vs NodeView) and Newtonsoft dialect parity are pinned by
/// <c>JsonPathWalkerParityTests</c> and the Newtonsoft-oracle <c>ReadParityTests</c>; fix the
/// walker, never the oracle.
/// </summary>
internal static class JsonPathWalker
{
    /// <summary>
    /// Selects the matches of <paramref name="jsonPath"/> against <paramref name="root"/>,
    /// pairing each matched view with its canonical JSONPath location relative to the root.
    /// </summary>
    /// <typeparam name="TView">The struct view type backing the walk.</typeparam>
    /// <param name="root">Root view to evaluate against.</param>
    /// <param name="jsonPath">JSONPath expression (bare, leading-dot, or rooted; normalized internally).</param>
    /// <returns>Matches in document order, each with its canonical path. Empty if nothing matches.</returns>
    public static IEnumerable<(TView Match, string CanonicalPath)> Select<TView>(TView root, string jsonPath)
        where TView : struct, IJsonView<TView>
    {
        var expression = JsonPathParser.Parse(JsonNodePath.NormalizePathOrRelative(jsonPath));

        IEnumerable<(TView Match, string CanonicalPath)> current = new[] { (root, "$") };

        foreach (var segment in expression.Segments)
        {
            current = segment switch
            {
                RootSegment => current, // already at root
                PropertySegment p => SelectProperty(current, p.Name),
                IndexSegment i => SelectIndex(current, i.Index),
                WildcardSegment => SelectWildcard(current),
                RecursiveDescentSegment => SelectRecursive(current),
                FilterSegment f => SelectFilter(current, f),
                _ => throw new NotImplementedException($"Segment {segment.GetType().Name} not yet supported")
            };
        }

        return current;
    }

    private static IEnumerable<(TView Match, string CanonicalPath)> SelectProperty<TView>(
        IEnumerable<(TView Match, string CanonicalPath)> input, string name)
        where TView : struct, IJsonView<TView>
    {
        foreach (var (match, path) in input)
        {
            if (match.Kind == JsonValueKind.Object)
            {
                if (match.TryGetProperty(name, out var child))
                {
                    yield return (child, path + "." + name);
                }
            }
            else if (match.Kind == JsonValueKind.String && RtCkIdJsonShim.IsVirtualProperty(name))
            {
                // CK identifier compatibility: RtCkId<T> serializes to a string
                // (SemanticVersionedFullName) via RtCkIdConverter. Legacy YAML drills into
                // `.SemanticVersionedFullName` / `.FullName` expecting the historical
                // reflection-emitted object shape. Return the string itself so those paths
                // keep resolving without inflating the wire format.
                yield return (match, path + "." + name);
            }
        }
    }

    private static IEnumerable<(TView Match, string CanonicalPath)> SelectIndex<TView>(
        IEnumerable<(TView Match, string CanonicalPath)> input, int index)
        where TView : struct, IJsonView<TView>
    {
        foreach (var (match, path) in input)
        {
            if (match.Kind != JsonValueKind.Array) continue;
            if (match.TryGetIndex(index, out var child))
            {
                yield return (child, path + "[" + index + "]");
            }
        }
    }

    private static IEnumerable<(TView Match, string CanonicalPath)> SelectWildcard<TView>(
        IEnumerable<(TView Match, string CanonicalPath)> input)
        where TView : struct, IJsonView<TView>
    {
        foreach (var (match, path) in input)
        {
            if (match.Kind == JsonValueKind.Array)
            {
                var idx = 0;
                foreach (var (_, value) in match.EnumerateChildren())
                {
                    yield return (value, path + "[" + idx + "]");
                    idx++;
                }
            }
            else if (match.Kind == JsonValueKind.Object)
            {
                foreach (var (key, value) in match.EnumerateChildren())
                {
                    yield return (value, path + "." + key);
                }
            }
        }
    }

    private static IEnumerable<(TView Match, string CanonicalPath)> SelectRecursive<TView>(
        IEnumerable<(TView Match, string CanonicalPath)> input)
        where TView : struct, IJsonView<TView>
    {
        foreach (var (match, path) in input)
        {
            foreach (var d in DescendAll(match, path)) yield return d;
        }
    }

    private static IEnumerable<(TView Match, string CanonicalPath)> SelectFilter<TView>(
        IEnumerable<(TView Match, string CanonicalPath)> input, FilterSegment filter)
        where TView : struct, IJsonView<TView>
    {
        // The filter predicate applies to the direct children of every node we are handed.
        // For arrays that means each element; for objects each property value. Under recursive
        // descent (`$..[?(...)]`) the descent provides the multiplicity and this segment provides
        // the predicate. Every yield's canonical path is built from the parent's path plus the
        // child's own index or property name, so every yield is path-distinct by construction.
        foreach (var (match, path) in input)
        {
            if (match.Kind == JsonValueKind.Array)
            {
                var idx = 0;
                foreach (var (_, value) in match.EnumerateChildren())
                {
                    if (FilterMatches(value, filter))
                    {
                        yield return (value, path + "[" + idx + "]");
                    }

                    idx++;
                }
            }
            else if (match.Kind == JsonValueKind.Object)
            {
                foreach (var (key, value) in match.EnumerateChildren())
                {
                    if (FilterMatches(value, filter))
                    {
                        yield return (value, path + "." + key);
                    }
                }
            }
        }
    }

    private static bool FilterMatches<TView>(TView candidate, FilterSegment filter)
        where TView : struct, IJsonView<TView>
    {
        var node = candidate;
        foreach (var prop in filter.RelativeProperty)
        {
            if (node.Kind != JsonValueKind.Object) return false;
            if (!node.TryGetProperty(prop, out node)) return false;
        }

        return node.TryGetString(out var value) && value == filter.Literal;
    }

    private static IEnumerable<(TView Match, string CanonicalPath)> DescendAll<TView>(TView match, string path)
        where TView : struct, IJsonView<TView>
    {
        yield return (match, path);
        switch (match.Kind)
        {
            case JsonValueKind.Object:
                foreach (var (key, value) in match.EnumerateChildren())
                {
                    var childPath = path + "." + key;
                    foreach (var d in DescendAll(value, childPath)) yield return d;
                }

                break;
            case JsonValueKind.Array:
                var idx = 0;
                foreach (var (_, value) in match.EnumerateChildren())
                {
                    var childPath = path + "[" + idx + "]";
                    foreach (var d in DescendAll(value, childPath)) yield return d;
                    idx++;
                }

                break;
        }
    }
}
