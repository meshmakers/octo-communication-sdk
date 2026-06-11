using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.JsonPath;

/// <summary>
/// Pins the generic <see cref="JsonPathWalker"/>'s cross-view consistency. For every case the
/// walker must, over both backing views, produce:
/// <list type="number">
/// <item>cross-view parity — <see cref="ElementView"/> matches equal <see cref="NodeView"/> matches
/// (values, in document order);</item>
/// <item>canonical-path parity — the canonical paths emitted over both views are identical and
/// well-formed (rooted at <c>$</c>).</item>
/// </list>
/// The legacy per-representation read walkers were retired; Newtonsoft dialect parity is now
/// enforced separately by <c>Sdk.Common.PipelineParityTests/ReadParityTests</c> (Newtonsoft is the
/// oracle there). Fix <see cref="JsonPathWalker"/>, never the test.
/// </summary>
public class JsonPathWalkerParityTests
{
    /// <summary>
    /// The shared parity corpus (every segment kind) plus cases that specifically exercise
    /// canonical-path emission and the RtCkId SemanticVersionedFullName/FullName self-alias shim.
    /// </summary>
    public static IEnumerable<object[]> Cases() => new[]
    {
        // ---- shared corpus (every segment kind) ----
        // property + index
        new object[] { """{"a":{"b":[1,2,3]}}""", "$.a.b[1]" },
        // wildcard on array
        new object[] { """{"a":[1,2,3]}""", "$.a[*]" },
        // wildcard on object (bracket form)
        new object[] { """{"a":{"x":1,"y":2}}""", "$.a[*]" },
        // recursive descent
        new object[] { """{"a":{"b":{"c":1}}}""", "$..c" },
        // filter on array elements
        new object[] { """[{"k":"x"},{"k":"y"}]""", "$[?(@.k=='x')]" },
        // filter on object members
        new object[] { """{"machines":{"m1":{"k":"x"},"m2":{"k":"y"}}}""", "$.machines[?(@.k=='x')]" },
        // filter under recursive descent
        new object[] { """{"a":{"items":[{"k":"x"},{"k":"y"}]}}""", "$..[?(@.k=='x')]" },
        // bracket-property at root
        new object[] { """{"foo-bar":42}""", "$['foo-bar']" },
        // RtCkId shim
        new object[] { """{"rec":"System.Foo/Bar-1"}""", "$.rec.SemanticVersionedFullName" },
        new object[] { """{"rec":"System.Foo/Bar-1"}""", "$.rec.FullName" },

        // ---- additional canonical-path / structural coverage ----
        // root only
        new object[] { """{"a":1}""", "$" },
        // nested property
        new object[] { """{"a":{"b":{"c":42}}}""", "$.a.b.c" },
        // wildcard then property (canonical path threading through array index)
        new object[] { """{"items":[{"n":1},{"n":2}]}""", "$.items[*].n" },
        // recursive descent over arrays and objects
        new object[] { """{"a":[{"c":1},{"c":2}],"b":{"c":3}}""", "$..c" },
        // recursive descent then property that does not exist anywhere (empty result)
        new object[] { """{"a":{"b":[1,2]},"c":3}""", "$..x" },
        // index into nested array
        new object[] { """{"grid":[[0,1],[2,3]]}""", "$.grid[1][0]" },
        // filter under recursive descent landing on object-keyed map
        new object[] { """{"plants":{"p1":{"k":"x","kids":{"a":{"k":"x"}}},"p2":{"k":"y"}}}""", "$..[?(@.k=='x')]" },
        // nested filter relative property
        new object[] { """[{"meta":{"type":"t"}},{"meta":{"type":"u"}}]""", "$[?(@.meta.type=='t')]" },
        // bracket-property mid-path
        new object[] { """{"a":{"foo bar":7}}""", "$.a['foo bar']" },
        // double-quote literal in filter
        new object[] { """[{"k":"x"},{"k":"y"}]""", """$[?(@.k=="y")]""" },
        // path with no match
        new object[] { """{"a":1}""", "$.missing" },
        // index out of range
        new object[] { """{"a":[1,2]}""", "$.a[5]" },

        // ---- chained wildcards with deep canonical paths ----
        // (exercises per-parent `idx` reset for canonical-path threading; distinct from $..)
        // array-of-arrays, double wildcard
        new object[] { """{"a":[[1,2],[3]]}""", "$.a[*][*]" },
        // array-of-objects, wildcard then property then wildcard
        new object[] { """{"a":[{"b":[1,2]},{"b":[3]}]}""", "$.a[*].b[*]" },

        // ---- RtCkId shim under wildcard and under recursive descent ----
        // shim fires per array element, each with its own canonical path
        new object[] { """{"recs":["System.Foo/Bar-1","System.X/Y-2"]}""", "$.recs[*].SemanticVersionedFullName" },
        // shim under recursive descent: descent visits every node (incl. string leaves),
        // SelectProperty("FullName") self-aliases string leaves AND reads real FullName props
        new object[] { """{"a":"System.Foo/Bar-1","b":{"FullName":"X.Y/Z-3"},"c":["System.P/Q-4"]}""", "$..FullName" },
        // shim under wildcard on object members (string values self-alias)
        new object[] { """{"m":{"r1":"System.A/B-1","r2":"System.C/D-2"}}""", "$.m[*].SemanticVersionedFullName" },

        // ---- nice-to-have ----
        // array-of-arrays under recursive descent (descent threads [idx][idx] canonical paths)
        new object[] { """{"a":[[1],[2]]}""", "$..a" },
        // number-valued filter target pins the non-string TryGetString branch:
        // @.k is a number so the shim/compare must NOT match the string literal '1'
        new object[] { """[{"k":1},{"k":"x"}]""", "$[?(@.k=='1')]" },
        // number-valued filter target that DOES have a string sibling matching the literal
        new object[] { """[{"k":1},{"k":"1"}]""", "$[?(@.k=='1')]" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Walker_IsConsistent_AcrossViews(string json, string path)
    {
        // Generic walker over ElementView.
        using var elemDoc = JsonDocument.Parse(json);
        var elemResults = JsonPathWalker.Select(new ElementView(elemDoc.RootElement), path).ToList();
        var elemMatches = elemResults.Select(r => Normalize(r.Match.GetRawText())).ToList();
        var elemPaths = elemResults.Select(r => r.CanonicalPath).ToList();

        // Generic walker over NodeView.
        var node = JsonNode.Parse(json);
        var nodeResults = JsonPathWalker.Select(new NodeView(node), path).ToList();
        var nodeMatches = nodeResults.Select(r => Normalize(r.Match.GetRawText())).ToList();
        var nodePaths = nodeResults.Select(r => r.CanonicalPath).ToList();

        // 1. Cross-view parity: ElementView == NodeView (matches and canonical paths, document order).
        Assert.Equal(elemMatches, nodeMatches);
        Assert.Equal(elemPaths, nodePaths);

        // 2. Canonical-path self-consistency: every emitted canonical path is rooted at '$'.
        Assert.All(elemPaths, p => Assert.StartsWith("$", p));
    }

    /// <summary>
    /// Golden-value pins for the exact canonical-path strings the walker emits. The legacy
    /// <c>JsonPathEvaluator</c> oracle (which produced these <c>$.x</c> / <c>$[i]</c> strings) has
    /// been deleted, yet the canonical paths remain load-bearing — <c>UpdateMatchesAsync</c> uses
    /// them for write-back. This test guards the path format with no external oracle: the expected
    /// arrays below are the documented format, asserted verbatim and in document order. They must
    /// also be identical across the <see cref="ElementView"/> and <see cref="NodeView"/> walks.
    /// Fix <see cref="JsonPathWalker"/>, never the test.
    /// </summary>
    public static IEnumerable<object[]> CanonicalPathCases() => new[]
    {
        new object[] { """{"a":{"b":7}}""", "$.a.b", new[] { "$.a.b" } },
        new object[] { """{"a":[10,20,30]}""", "$.a[1]", new[] { "$.a[1]" } },
        new object[] { """{"a":[1,2,3]}""", "$.a[*]", new[] { "$.a[0]", "$.a[1]", "$.a[2]" } },
        new object[] { """{"a":{"x":1},"b":{"x":2}}""", "$..x", new[] { "$.a.x", "$.b.x" } },
        new object[]
        {
            """{"a":[{"b":[1,2]},{"b":[3]}]}""", "$.a[*].b[*]",
            new[] { "$.a[0].b[0]", "$.a[0].b[1]", "$.a[1].b[0]" }
        },
        new object[]
        {
            """{"items":[{"v":1},{"v":2}]}""", "$.items[*].v",
            new[] { "$.items[0].v", "$.items[1].v" }
        },
    };

    [Theory]
    [MemberData(nameof(CanonicalPathCases))]
    public void CanonicalPaths_AreExact(string json, string path, string[] expectedCanonicalPaths)
    {
        // ElementView walk: assert exact canonical-path strings, in document order.
        var elem = JsonDocument.Parse(json).RootElement;
        var elemPaths = JsonPathWalker.Select(new ElementView(elem), path)
            .Select(m => m.CanonicalPath).ToArray();
        Assert.Equal(expectedCanonicalPaths, elemPaths);

        // NodeView walk must emit the SAME canonical paths (element/node parity).
        var nodePaths = JsonPathWalker.Select(new NodeView(JsonNode.Parse(json)), path)
            .Select(m => m.CanonicalPath).ToArray();
        Assert.Equal(expectedCanonicalPaths, nodePaths);
    }

    private static string Normalize(string raw)
    {
        // STJ and JsonElement.GetRawText can differ in whitespace; round-trip through
        // JsonDocument to collapse to a canonical form.
        using var doc = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}
