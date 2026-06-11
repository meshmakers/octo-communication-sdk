using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

public class DataContextTryGetTests
{
    private static DataContextImpl Ctx(string json) =>
        new DataContextImpl(JsonDocument.Parse(json));

    [Fact]
    public void TryGet_PresentValue_ReturnsTrueAndValue()
    {
        using var ctx = Ctx("{\"a\": 5}");
        var found = ctx.TryGet<int>("$.a", out var value);
        Assert.True(found);
        Assert.Equal(5, value);
    }

    [Fact]
    public void TryGet_MissingPath_ReturnsFalse()
    {
        using var ctx = Ctx("{\"a\": 1}");
        var found = ctx.TryGet<int>("$.missing", out var value);
        Assert.False(found);
        Assert.Equal(default(int), value);
    }

    [Fact]
    public void TryGet_ExplicitNull_ReturnsTrueWithDefault()
    {
        // Explicit null should report present (Exists == true), return true with null/default value
        using var ctx = Ctx("{\"a\": null}");
        var found = ctx.TryGet<string>("$.a", out var value);
        Assert.True(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_String_ReturnsTrueAndValue()
    {
        using var ctx = Ctx("{\"name\": \"hello\"}");
        var found = ctx.TryGet<string>("$.name", out var value);
        Assert.True(found);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void TryGet_AfterOverlaySet_ReturnsTrueAndOverlayValue()
    {
        using var ctx = Ctx("{\"x\": 1}");
        ctx.Set("$.x", 42);
        var found = ctx.TryGet<int>("$.x", out var value);
        Assert.True(found);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGet_AfterClear_ReturnsFalse()
    {
        using var ctx = Ctx("{\"x\": 1}");
        ctx.Clear("$.x");
        var found = ctx.TryGet<int>("$.x", out var value);
        Assert.False(found);
        Assert.Equal(default(int), value);
    }
}
