using System.Text.Json.Nodes;
using LiteDB;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Buffering;

public class LiteDbBsonConverterTests
{
    [Theory]
    [InlineData("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null}")]
    [InlineData("[1,\"two\",false,null,{\"x\":1}]")]
    [InlineData("\"scalar\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("{\"nested\":{\"a\":[1,2,3],\"b\":{\"c\":\"d\"}}}")]
    public void RoundTrip_PreservesValue(string json)
    {
        var node = JsonNode.Parse(json);
        var bson = LiteDbBsonConverter.ToBson(node);
        var roundTripped = LiteDbBsonConverter.FromBson(bson);

        Assert.Equal(node?.ToJsonString(), roundTripped?.ToJsonString());
    }

    [Theory]
    // Strings that *look like* DateTime/Guid/Base64 must round-trip as strings.
    // STJ's JsonValue.TryGetValue<DateTime>/Guid/byte[] on a JsonElement-backed
    // value will parse ISO-8601/Guid-shaped/base64-shaped strings into typed
    // values; coercing them to BSON DateTime/Guid/Binary changes the round-trip
    // shape (e.g. "2024-01-01" → "2024-01-01T00:00:00"). Same fix shape as
    // commit 00ed665 in DistinctNode.
    [InlineData("{\"date\":\"2024-01-01\"}")]
    [InlineData("{\"datetime\":\"2024-01-01T12:34:56\"}")]
    [InlineData("{\"guid\":\"12345678-1234-1234-1234-123456789012\"}")]
    [InlineData("{\"b64\":\"aGVsbG8=\"}")] // "hello" base64
    public void RoundTrip_StringsThatLookLikeTypedValues_PreservedAsStrings(string json)
    {
        var node = JsonNode.Parse(json);
        var bson = LiteDbBsonConverter.ToBson(node);
        var roundTripped = LiteDbBsonConverter.FromBson(bson);

        Assert.Equal(node?.ToJsonString(), roundTripped?.ToJsonString());
    }

    [Fact]
    public void ToBson_NullNode_ReturnsBsonNull()
    {
        var bson = LiteDbBsonConverter.ToBson(null);
        Assert.True(bson.IsNull);
    }

    [Fact]
    public void FromBson_NullValue_ReturnsNull()
    {
        Assert.Null(LiteDbBsonConverter.FromBson(BsonValue.Null));
        Assert.Null(LiteDbBsonConverter.FromBson(null));
    }

    [Fact]
    public void ToDictionary_NullNode_ReturnsEmptyDictionary()
    {
        var dict = LiteDbBsonConverter.ToDictionary(null);
        Assert.Empty(dict);
    }

    [Fact]
    public void ToDictionary_NonObjectNode_Throws()
    {
        var arr = JsonNode.Parse("[1,2,3]");
        Assert.Throws<ArgumentException>(() => LiteDbBsonConverter.ToDictionary(arr));
    }

    [Fact]
    public void ToDictionary_FlatObject_ProducesEntries()
    {
        var node = JsonNode.Parse("{\"a\":1,\"b\":\"two\",\"c\":true}");
        var dict = LiteDbBsonConverter.ToDictionary(node);

        Assert.Equal(3, dict.Count);
        Assert.Equal(1L, dict["a"].AsInt64);
        Assert.Equal("two", dict["b"].AsString);
        Assert.True(dict["c"].AsBoolean);
    }

    [Fact]
    public void FromDictionary_RoundTripsBackToObject()
    {
        var node = (JsonObject?)JsonNode.Parse("{\"a\":1,\"b\":\"two\",\"c\":true}");
        var dict = LiteDbBsonConverter.ToDictionary(node);

        var roundTripped = LiteDbBsonConverter.FromDictionary(dict);

        Assert.Equal(node?.ToJsonString(), roundTripped.ToJsonString());
    }

    [Fact]
    public void ToBson_IntegerValuedNumber_StoredAsInt64()
    {
        var node = JsonNode.Parse("42");
        var bson = LiteDbBsonConverter.ToBson(node);

        Assert.True(bson.IsInt32 || bson.IsInt64,
            $"Expected integer BSON type but got {bson.Type}");
    }

    [Fact]
    public void LiteDbBsonConverter_IntegerValuedDouble_PreservesDoubleType()
    {
        // L7: A JSON number written as `1.0` (with explicit decimal point) is a double
        // in the source document. The converter must preserve double type so the
        // round-trip JSON shape matches the input — silently widening to long produces
        // `1` on read-back, which is observable to downstream consumers.
        var node = JsonNode.Parse("{\"v\": 1.0}");
        var bson = LiteDbBsonConverter.ToBson(node);
        var roundTripped = LiteDbBsonConverter.FromBson(bson);

        Assert.NotNull(roundTripped);
        var v = roundTripped!["v"];
        Assert.NotNull(v);
        // The value must still be a double-kind number after round-trip.
        Assert.Equal(System.Text.Json.JsonValueKind.Number, v!.GetValueKind());
        Assert.Equal(1.0, v!.GetValue<double>());
    }

    [Fact]
    public void ToBson_FractionalNumber_StoredAsDouble()
    {
        var node = JsonNode.Parse("3.14");
        var bson = LiteDbBsonConverter.ToBson(node);

        Assert.True(bson.IsDouble || bson.IsDecimal,
            $"Expected floating-point BSON type but got {bson.Type}");
        Assert.Equal(3.14, bson.AsDouble, 5);
    }

    [Fact]
    public void MergeDictionaries_OverlappingScalars_WrapsInArray()
    {
        var a = LiteDbBsonConverter.ToDictionary(JsonNode.Parse("{\"x\":1}"));
        var b = LiteDbBsonConverter.ToDictionary(JsonNode.Parse("{\"x\":2}"));

        var merged = LiteDbBsonConverter.MergeDictionaries(a, b);

        Assert.True(merged["x"].IsArray);
        Assert.Equal(2, merged["x"].AsArray.Count);
    }

    [Fact]
    public void MergeDictionaries_DisjointKeys_PreservesAll()
    {
        var a = LiteDbBsonConverter.ToDictionary(JsonNode.Parse("{\"x\":1}"));
        var b = LiteDbBsonConverter.ToDictionary(JsonNode.Parse("{\"y\":2}"));

        var merged = LiteDbBsonConverter.MergeDictionaries(a, b);

        Assert.Equal(2, merged.Count);
        Assert.Equal(1L, merged["x"].AsInt64);
        Assert.Equal(2L, merged["y"].AsInt64);
    }
}
