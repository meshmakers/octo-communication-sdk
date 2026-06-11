using System;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

public class DataContextGetValueTests
{
    private static DataContextImpl Ctx(string json) =>
        new DataContextImpl(JsonDocument.Parse(json));

    [Fact]
    public void GetValue_SmallInteger_ReturnsInt()
    {
        // Newtonsoft parity: JObject.FromObject(int 42) preserves Int32 in the JValue, and
        // the production round-trip via RtNewtonsoftAttributesConverter returns int. JsonScalar
        // matches by preferring Int32 over Int64 for values that fit. Enforced by
        // Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests.
        using var ctx = Ctx("{\"v\": 42}");
        var result = ctx.GetValue("$.v");
        Assert.Equal(42, result);
        Assert.IsType<int>(result);
    }

    [Fact]
    public void GetValue_LargeInteger_ReturnsLong()
    {
        // Values that don't fit in Int32 fall through to Int64 — matches Newtonsoft's behaviour
        // for the same source CLR type.
        using var ctx = Ctx("{\"v\": 2147483648}"); // int.MaxValue + 1
        var result = ctx.GetValue("$.v");
        Assert.Equal(2147483648L, result);
        Assert.IsType<long>(result);
    }

    [Fact]
    public void GetValue_Real_ReturnsDouble()
    {
        using var ctx = Ctx("{\"v\": 3.5}");
        var result = ctx.GetValue("$.v");
        Assert.Equal(3.5d, result);
        Assert.IsType<double>(result);
    }

    [Fact]
    public void GetValue_IsoString_ReturnsDateTime_WhenParseDateStringsTrue()
    {
        using var ctx = Ctx("{\"v\": \"2024-06-15T10:00:00\"}");
        var result = ctx.GetValue("$.v", parseDateStrings: true);
        Assert.IsType<DateTime>(result);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0), result);
    }

    [Fact]
    public void GetValue_IsoString_ReturnsString_WhenParseDateStringsFalse()
    {
        using var ctx = Ctx("{\"v\": \"2024-06-15T10:00:00\"}");
        var result = ctx.GetValue("$.v", parseDateStrings: false);
        Assert.IsType<string>(result);
        Assert.Equal("2024-06-15T10:00:00", result);
    }

    [Fact]
    public void GetValue_PlainString_ReturnsString()
    {
        using var ctx = Ctx("{\"v\": \"hello\"}");
        var result = ctx.GetValue("$.v");
        Assert.Equal("hello", result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void GetValue_Bool_ReturnsBool()
    {
        using var ctx = Ctx("{\"v\": true}");
        var result = ctx.GetValue("$.v");
        Assert.Equal(true, result);
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetValue_MissingPath_ReturnsNull()
    {
        using var ctx = Ctx("{\"a\": 1}");
        var result = ctx.GetValue("$.missing");
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_ObjectAtPath_ReturnsNull()
    {
        using var ctx = Ctx("{\"v\": {\"nested\": 1}}");
        var result = ctx.GetValue("$.v");
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_ArrayAtPath_ReturnsNull()
    {
        using var ctx = Ctx("{\"v\": [1, 2, 3]}");
        var result = ctx.GetValue("$.v");
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_WorksAfterOverlayWrite()
    {
        using var ctx = Ctx("{\"v\": 1}");
        ctx.Set("$.v", 99L);
        var result = ctx.GetValue("$.v");
        // Writing 99L produces JSON "99" — indistinguishable from int on the wire — and the
        // read path boxes Int32 first (Newtonsoft-parity rule). long 99 → JSON 99 → int 99.
        Assert.Equal(99, result);
        Assert.IsType<int>(result);
    }

    [Fact]
    public void GetValue_NullJson_ReturnsNull()
    {
        using var ctx = Ctx("{\"v\": null}");
        var result = ctx.GetValue("$.v");
        Assert.Null(result);
    }
}
