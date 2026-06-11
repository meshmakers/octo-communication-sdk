using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Phase-0 characterization: <see cref="IDataContext.GetValue"/> has two read paths in
/// <see cref="DataContextImpl"/> — a fast path that reads the backing
/// <see cref="JsonElement"/> directly (overlay has no writes) and a slow path that reads
/// the lifted <see cref="JsonNode"/> overlay. Both funnel through
/// <c>JsonScalar.ToClr</c>, so they MUST produce the same boxed CLR type and value. This
/// guards generalizing the fast path to the child during unification.
/// </summary>
public class GetValueFastSlowParityTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("5")]
    [InlineData("9000000000")]
    [InlineData("1.5")]
    [InlineData("2.0")]
    [InlineData("true")]
    [InlineData("\"2026-05-29T10:00:00Z\"")]
    public void GetValue_ElementPath_And_NodePath_Agree(string jsonValue)
    {
        // Element-backed (fast path): overlay has no writes, GetValue reads the
        // JsonElement directly.
        using var elementCtx = new DataContextImpl(Doc($"{{\"v\": {jsonValue}}}"));
        var fromElement = elementCtx.GetValue("$.v");

        // Node-backed (slow path): seed the value via Set so the overlay lifts and
        // GetValue reads the JsonNode instead. Set("$.v", <JsonNode>) stores the node
        // verbatim (DeepClone), forcing the JsonValue branch in GetValue.
        using var nodeCtx = new DataContextImpl(Doc("{}"));
        nodeCtx.Set("$.v", JsonNode.Parse(jsonValue));
        var fromNode = nodeCtx.GetValue("$.v");

        Assert.Equal(fromElement, fromNode);
        Assert.Equal(fromElement?.GetType(), fromNode?.GetType());
    }
}
