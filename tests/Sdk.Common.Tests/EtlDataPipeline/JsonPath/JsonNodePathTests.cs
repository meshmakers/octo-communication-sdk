using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.JsonPath;

/// <summary>
/// Covers the write/normalize surface that <see cref="JsonNodePath"/> retains after the
/// read walkers were retired: <c>Set</c>, <c>Remove</c>, and <c>NormalizePathOrRelative</c>.
/// JSONPath reads are exercised by <c>JsonPathWalkerParityTests</c> and the Newtonsoft-oracle
/// <c>ReadParityTests</c> against the generic <c>JsonPathWalker</c>.
/// </summary>
public class JsonNodePathTests
{
    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    // -------------------------------------------------------------------
    // Set
    // -------------------------------------------------------------------

    [Fact]
    public void Set_DirectProperty_AssignsValue()
    {
        var root = new JsonObject();
        JsonNodePath.Set(root, "$.x", JsonValue.Create(1));
        Assert.Equal(1, root["x"]!.GetValue<int>());
    }

    [Fact]
    public void Set_NestedPath_AutoCreatesIntermediates()
    {
        var root = new JsonObject();
        JsonNodePath.Set(root, "$.a.b.c", JsonValue.Create(42));

        Assert.IsType<JsonObject>(root["a"]);
        Assert.IsType<JsonObject>(root["a"]!["b"]);
        Assert.Equal(42, root["a"]!["b"]!["c"]!.GetValue<int>());
    }

    [Fact]
    public void Set_ExistingProperty_OverwritesValue()
    {
        var root = Parse("{\"x\":1}").AsObject();
        JsonNodePath.Set(root, "$.x", JsonValue.Create(99));
        Assert.Equal(99, root["x"]!.GetValue<int>());
    }

    [Fact]
    public void Set_NonObjectIntermediate_Throws()
    {
        var root = Parse("{\"x\":1}").AsObject();
        Assert.Throws<JsonPathNotSupportedException>(
            () => JsonNodePath.Set(root, "$.x.y", JsonValue.Create(2)));
    }

    [Theory]
    [InlineData("$.items[0].name")]
    [InlineData("$.items[*]")]
    [InlineData("$.items[?(@.Id == 'x')]")]
    [InlineData("$..x")]
    public void Set_RejectsIllegalSegments(string path)
    {
        var root = new JsonObject();
        Assert.Throws<JsonPathNotSupportedException>(
            () => JsonNodePath.Set(root, path, JsonValue.Create(1)));
    }

    // -------------------------------------------------------------------
    // Remove
    // -------------------------------------------------------------------

    [Fact]
    public void Remove_DirectProperty_RemovesAndReturnsTrue()
    {
        var root = Parse("{\"x\":1}").AsObject();
        var removed = JsonNodePath.Remove(root, "$.x");
        Assert.True(removed);
        Assert.False(root.ContainsKey("x"));
    }

    [Fact]
    public void Remove_NestedProperty_RemovesAndReturnsTrue()
    {
        var root = Parse("{\"a\":{\"b\":1}}").AsObject();
        var removed = JsonNodePath.Remove(root, "$.a.b");
        Assert.True(removed);
        Assert.IsType<JsonObject>(root["a"]);
        Assert.False(root["a"]!.AsObject().ContainsKey("b"));
    }

    [Fact]
    public void Remove_MissingProperty_ReturnsFalse()
    {
        var root = Parse("{\"x\":1}").AsObject();
        var removed = JsonNodePath.Remove(root, "$.missing");
        Assert.False(removed);
        Assert.True(root.ContainsKey("x"));
    }

    [Fact]
    public void Remove_NestedPathWithMissingIntermediate_ReturnsFalse()
    {
        var root = Parse("{\"x\":1}").AsObject();
        var removed = JsonNodePath.Remove(root, "$.a.b");
        Assert.False(removed);
    }

    [Theory]
    [InlineData("$.items[0]")]
    [InlineData("$.items[*]")]
    [InlineData("$.items[?(@.Id == 'x')]")]
    [InlineData("$..x")]
    public void Remove_RejectsIllegalSegments(string path)
    {
        var root = new JsonObject();
        Assert.Throws<JsonPathNotSupportedException>(
            () => JsonNodePath.Remove(root, path));
    }

    // -------------------------------------------------------------------
    // Path normalization (Phase 2.5.4)
    // Pre-2.5.2 bespoke walkers tolerated bare ("id"), leading-dot (".id"),
    // and rooted ("$.id") path strings interchangeably. JsonPathParser.Parse
    // requires the $ root prefix; NormalizePath restores legacy tolerance.
    // -------------------------------------------------------------------

    [Fact]
    public void JsonNodePath_Set_BareDottedPath_Normalizes()
    {
        var root = new JsonObject();
        JsonNodePath.Set(root, "foo.bar", JsonValue.Create(7));
        Assert.Equal(7, root["foo"]!["bar"]!.GetValue<int>());
    }

    [Fact]
    public void JsonNodePath_Set_LeadingDotPath_Normalizes()
    {
        var root = new JsonObject();
        JsonNodePath.Set(root, ".foo.bar", JsonValue.Create(11));
        Assert.Equal(11, root["foo"]!["bar"]!.GetValue<int>());
    }

    [Fact]
    public void JsonNodePath_Remove_BareDottedPath_Normalizes()
    {
        var root = Parse("{\"foo\":{\"bar\":1}}").AsObject();
        var removed = JsonNodePath.Remove(root, "foo.bar");
        Assert.True(removed);
        Assert.False(root["foo"]!.AsObject().ContainsKey("bar"));
    }

    // -------------------------------------------------------------------
    // NormalizePathOrRelative — public canonical normalizer (Task 9)
    // Replaces the per-node NormalizeRelative duplicates in transform nodes.
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("", "$")]
    [InlineData("$", "$")]
    [InlineData("$.id", "$.id")]
    [InlineData("$[0]", "$[0]")]
    [InlineData("$['foo-bar']", "$['foo-bar']")]
    [InlineData(".id", "$.id")]
    [InlineData("id", "$.id")]
    [InlineData("foo.bar", "$.foo.bar")]
    public void NormalizePathOrRelative_MatchesNodeNormalizeRelativeContract(string input, string expected)
    {
        Assert.Equal(expected, JsonNodePath.NormalizePathOrRelative(input));
    }
}
