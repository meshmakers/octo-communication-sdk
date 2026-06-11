using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Internal;
using Newtonsoft.Json.Linq;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms.Internal;

/// <summary>
/// Asserts <see cref="JsonStringifyHelper.ToLegacyString"/> matches Newtonsoft's
/// <c>JToken.ToString()</c> default (Formatting.Indented for objects/arrays,
/// unquoted scalars). Hash stability across the Newtonsoft→STJ migration depends
/// on this — HashNode uses ToLegacyString to compute its input string.
/// </summary>
public class JsonStringifyHelperTests
{
    [Fact]
    public void Helper_is_internal() =>
        // Intentionally internal: every consumer (HashNode, Base64EncodeNode, ConcatNode,
        // FormatStringNode, PrintDebugNode) lives in Sdk.Common. It was briefly made public for
        // cross-assembly reuse by mesh-adapter, but mesh-adapter's stringification sites correctly
        // use compact JSON (Newtonsoft Formatting.None parity), not this indented helper — so the
        // public surface was unnecessary. Tests reach it via [InternalsVisibleTo].
        Assert.True(typeof(JsonStringifyHelper).IsNotPublic);

    [Fact]
    public void ToLegacyString_Null_ReturnsNull()
    {
        Assert.Null(JsonStringifyHelper.ToLegacyString(null));
    }

    [Theory]
    [InlineData("true", "True")]
    [InlineData("false", "False")]
    public void ToLegacyString_Boolean_CapitalizedToMatchNewtonsoft(string json, string expected)
    {
        var node = JsonNode.Parse(json);
        Assert.Equal(expected, JsonStringifyHelper.ToLegacyString(node));
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("\"\"", "")]
    [InlineData("\"with \\\"quotes\\\"\"", "with \"quotes\"")]
    public void ToLegacyString_String_Unquoted(string json, string expected)
    {
        var node = JsonNode.Parse(json);
        Assert.Equal(expected, JsonStringifyHelper.ToLegacyString(node));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("-42.5")]
    public void ToLegacyString_Number_PreservesLiteral(string json)
    {
        var node = JsonNode.Parse(json);
        // Number formatting: STJ ToJsonString preserves the source literal — matches
        // Newtonsoft's behavior closely enough for hash stability.
        Assert.Equal(node!.ToJsonString(), JsonStringifyHelper.ToLegacyString(node));
    }

    [Theory]
    [InlineData("{\"a\":1,\"b\":2}")]
    [InlineData("{\"nested\":{\"x\":1,\"y\":[1,2,3]}}")]
    [InlineData("[1,2,3]")]
    [InlineData("[{\"id\":1},{\"id\":2}]")]
    public void ToLegacyString_ObjectOrArray_MatchesNewtonsoftIndented(string json)
    {
        // The docstring on JsonStringifyHelper says the helper provides "parity with
        // pre-migration output" for FormatStringNode, ConcatNode, HashNode. Newtonsoft's
        // JToken.ToString() defaults to Formatting.Indented; the STJ migration accidentally
        // dropped the indentation for objects/arrays by routing through ToJsonString()
        // (compact). Hash stability for object/array sources depends on this matching.
        var stj = JsonNode.Parse(json);
        var newtonsoft = JToken.Parse(json);

        Assert.Equal(newtonsoft.ToString(), JsonStringifyHelper.ToLegacyString(stj));
    }

    [Theory]
    [InlineData("{\"name\":\"Mühle & Co <x>\"}")]
    [InlineData("{\"city\":\"Größe\",\"note\":\"a&b\"}")]
    [InlineData("[\"Straße\",\"Café\"]")]
    public void ToLegacyString_ObjectOrArrayWithNonAscii_MatchesNewtonsoftLiteral(string json)
    {
        // STJ's default encoder (JavaScriptEncoder.Default) escapes all non-ASCII (ü→ü)
        // and HTML-sensitive chars (&→&, <→<); Newtonsoft's JToken.ToString() emits
        // them literally. HashNode hashes ToLegacyString output, so this escaping silently
        // diverges the hash post-migration for any object/array source containing umlauts
        // (routine on a German industrial platform). Parity requires the helper to emit
        // non-ASCII literally — i.e. JavaScriptEncoder.UnsafeRelaxedJsonEscaping.
        var stj = JsonNode.Parse(json);
        var newtonsoft = JToken.Parse(json);

        Assert.Equal(newtonsoft.ToString(), JsonStringifyHelper.ToLegacyString(stj));
    }
}
