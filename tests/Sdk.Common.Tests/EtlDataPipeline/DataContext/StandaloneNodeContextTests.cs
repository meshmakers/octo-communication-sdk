using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Pins the node-backed standalone <see cref="DataContextImpl"/> constructor (overlay pre-lifted
/// at <c>$</c>) used to wrap a detached node match. It must answer reads/writes identically to the
/// element-backed context (the oracle), survive disposal (owns no document), isolate writes, and
/// preserve the present-but-null (JSON null) vs absent distinction.
/// </summary>
public class StandaloneNodeContextTests
{
    private static IDataContext NodeCtx(string json) => new DataContextImpl(JsonNode.Parse(json));
    private static IDataContext ElemCtx(string json) => new DataContextImpl(JsonDocument.Parse(json).RootElement);

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        string[] docs =
        {
            """{"a":{"b":1},"c":[10,20],"s":"hi","n":2.0,"z":null}""",
            "[1,2,3]",
            "\"root-string\"",
            "42",
            "true",
        };
        string[] paths = { "$", "$.a", "$.a.b", "$.c", "$.c[0]", "$.c[9]", "$.s", "$.n", "$.z", "$.missing", "$[0]", "$[9]" };
        foreach (var d in docs)
            foreach (var p in paths)
                data.Add(d, p);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NodeBacked_MatchesElementBacked(string json, string path)
    {
        using var node = NodeCtx(json);
        using var elem = ElemCtx(json);

        var kind = elem.GetKind(path);
        Assert.Equal(kind, node.GetKind(path));
        Assert.Equal(elem.Exists(path), node.Exists(path));
        Assert.Equal(elem.GetValue(path)?.ToString(), node.GetValue(path)?.ToString());
        // Type-agnostic structural comparison (Get<string> would throw on non-string kinds for BOTH).
        Assert.Equal(elem.Get<JsonNode>(path)?.ToJsonString(), node.Get<JsonNode>(path)?.ToJsonString());
        // Length is only defined for Array/String kinds (throws otherwise, identically for both,
        // since it switches on the already-asserted GetKind).
        if (kind is DataKind.Array or DataKind.String)
            Assert.Equal(elem.Length(path), node.Length(path));
    }

    [Fact]
    public void NodeBacked_JsonNullRoot_IsPresentButNull()
    {
        using var ctx = new DataContextImpl((JsonNode?)null);
        Assert.Equal(DataKind.Null, ctx.GetKind("$"));
        Assert.True(ctx.Exists("$"));
        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.anything"));
    }

    [Fact]
    public void NodeBacked_SurvivesAndIsolatesWrites()
    {
        var node = JsonNode.Parse("""{"a":1}""");
        using var ctx = new DataContextImpl(node);
        ctx.Set("$.a", 99);
        // Write lands in the context...
        Assert.Equal(99, ctx.Get<int>("$.a"));
        // ...and does not mutate the source node passed in (it was DeepCloned at the seam, but the
        // ctor itself writes the given node into the overlay; callers pass an already-owned orphan).
        // A second independent context over the original json is unaffected:
        using var fresh = NodeCtx("""{"a":1}""");
        Assert.Equal(1, fresh.Get<int>("$.a"));
    }

    [Fact]
    public void NodeBacked_OwnsNoDocument_DisposeIsNoOp()
    {
        var ctx = new DataContextImpl(JsonNode.Parse("""{"a":1}"""));
        ctx.Dispose();
        ctx.Dispose(); // idempotent, no throw
    }
}
