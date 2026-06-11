using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Pins the <see cref="JsonDetach"/> / <see cref="DetachedMatch"/> primitive that replaces
/// the per-match <c>GetRawText()</c>/<c>ToJsonString()</c> + <c>Parse</c> round-trip across the
/// <c>IReadSource.Evaluate</c> seam. The primitive must:
/// (1) detach a match into a self-owned value that survives the source document's disposal,
/// (2) isolate the detached value from later mutations of the source,
/// (3) bridge a <see cref="JsonElement"/> to a <see cref="JsonNode"/> WITHOUT a UTF-16 string,
///     preserving the raw number/null token exactly (Newtonsoft-parity is settled here in
///     isolation — see <c>GetValueFastSlowParityTests</c> for the read-side oracle).
/// </summary>
public class DetachedMatchTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void DetachElement_SurvivesSourceDocumentDispose()
    {
        var doc = JsonDocument.Parse("""{"a":{"b":1}}""");
        var match = doc.RootElement.GetProperty("a");

        var detached = JsonDetach.Detach(new ElementView(match), "$.a");
        doc.Dispose(); // the source's pooled buffer is gone

        Assert.False(detached.IsNode);
        Assert.Equal("""{"b":1}""", detached.Element!.Value.GetRawText());
        Assert.Equal("$.a", detached.CanonicalPath);
    }

    [Fact]
    public void DetachNode_IsIndependentClone()
    {
        var root = new JsonObject { ["v"] = 1 };
        var detached = JsonDetach.Detach(new NodeView(root["v"]), "$.v");

        root["v"] = 99; // mutate the source after detaching

        Assert.True(detached.IsNode);
        Assert.Equal(1, detached.Node!.GetValue<int>());
    }

    [Fact]
    public void DetachNode_NullMatch_IsPresentNode()
    {
        // A JSON-null match is present-but-null (IsNode true, Node null) — distinct from no match.
        var detached = JsonDetach.Detach(new NodeView(null), "$.x");
        Assert.True(detached.IsNode);
        Assert.Null(detached.Node);
        Assert.Equal("$.x", detached.CanonicalPath);
    }

    [Fact]
    public void ToNode_NullElement_ReturnsNull()
    {
        Assert.Null(JsonDetach.ToNode(El("null")));
    }

    [Theory]
    [InlineData("0.0")]
    [InlineData("2.0")]
    [InlineData("42")]
    [InlineData("2147483648")]
    [InlineData("3.5")]
    [InlineData("1E20")]
    [InlineData("\"hello\"")]
    [InlineData("true")]
    public void ToNode_PreservesRawToken(string json)
    {
        // The element->node bridge must carry the raw JSON token verbatim (no .0 stripping,
        // no int<->double reshaping) — it is a structural copy, not a typed re-encode.
        var node = JsonDetach.ToNode(El(json));
        Assert.Equal(json, node!.ToJsonString());
    }

    [Fact]
    public void ToNode_PreservesObjectStructure()
    {
        var node = JsonDetach.ToNode(El("""{"a":1,"b":[2,3]}"""));
        Assert.Equal("""{"a":1,"b":[2,3]}""", node!.ToJsonString());
    }
}
