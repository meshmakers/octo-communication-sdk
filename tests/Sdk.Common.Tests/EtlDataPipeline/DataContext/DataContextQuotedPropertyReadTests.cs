using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// AB#7509 — a property name that is not an identifier (an OBIS code such as <c>32.7.0</c>, sent by smart-meter
/// modules) is addressed with a bracket-quoted path. Reading it must not depend on whether a node has written
/// to the document before: after the first write the overlay is lifted and the read went through the
/// ".name"/"[index]" navigation, which treated the quoted name as a missing index and yielded nothing (0 for a
/// <c>Get&lt;double&gt;</c>) - silently.
/// </summary>
public class DataContextQuotedPropertyReadTests
{
    private const string Doc = """{"msg":{"value":{"32.7.0":"234.20","a b":"x","plain":"p"}}}""";

    private static DataContextImpl Create(bool lifted)
    {
        var ctx = new DataContextImpl(JsonDocument.Parse(Doc));
        if (lifted)
        {
            ctx.Set("$.other.written", "1");
        }

        return ctx;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Get_QuotedPropertyWithDots_ReturnsTheValue_WhetherOrNotTheOverlayIsLifted(bool lifted)
    {
        var ctx = Create(lifted);

        Assert.Equal("234.20", ctx.Get<string>("$.msg.value['32.7.0']"));
        Assert.Equal("234.20", ctx.Get<string>("$.msg.value[\"32.7.0\"]"));
        Assert.Equal(234.2, ctx.Get<double>("$.msg.value['32.7.0']"));
        Assert.Equal("x", ctx.Get<string>("$.msg.value['a b']"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistsAndKind_QuotedProperty_AgreeWithTheDocument(bool lifted)
    {
        var ctx = Create(lifted);

        Assert.True(ctx.Exists("$.msg.value['32.7.0']"));
        Assert.Equal(DataKind.String, ctx.GetKind("$.msg.value['32.7.0']"));
        Assert.False(ctx.Exists("$.msg.value['1.8.0']"));
        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.msg.value['1.8.0']"));
    }

    [Fact]
    public void Get_QuotedProperty_OfAValueWrittenByANode_ReturnsTheValue()
    {
        var ctx = Create(lifted: false);
        ctx.Set("$.copy", JsonNode.Parse("""{"1.8.0":"12345.678"}"""));

        Assert.Equal(12345.678, ctx.Get<double>("$.copy['1.8.0']"));
        Assert.Equal("234.20", ctx.Get<string>("$.msg.value['32.7.0']"));
    }
}
