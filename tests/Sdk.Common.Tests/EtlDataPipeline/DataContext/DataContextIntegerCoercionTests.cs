using System;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Integer coercion on the typed read path (AB#5275). STJ's built-in Int32/Int64 converters inspect
/// the RAW TEXT of a number token, so the literal <c>5.0</c> was rejected although the value is
/// integral — which broke every pipeline that wrote a double (integral doubles are serialized with a
/// trailing <c>.0</c> by design) and read it back as Int.
/// </summary>
/// <remarks>
/// Every case is asserted twice — once on a clean context (zero-copy element-direct path,
/// <c>DataContext.Get&lt;T&gt;</c>) and once after an unrelated write lifted the overlay (node
/// fall-through path). Both must agree, mirroring
/// <see cref="GetTypedElementVsNodeParityTests" />. The rounding semantics themselves are pinned
/// against the Newtonsoft oracle by <c>Sdk.Common.PipelineParityTests.IntegerCoercionParityTests</c>.
/// </remarks>
public class DataContextIntegerCoercionTests
{
    public sealed record Point(int X, string Y);

    private static IDataContext Clean(string json) =>
        new DataContextImpl(JsonDocument.Parse(json).RootElement);

    private static IDataContext Lifted(string json)
    {
        var ctx = new DataContextImpl(JsonDocument.Parse(json).RootElement);
        ctx.Set("$.__lift", 1); // unrelated write flips HasWrites → reads take the node path
        return ctx;
    }

    /// <summary>Runs <paramref name="read" /> on both read paths and asserts they agree.</summary>
    private static void AssertBothPaths<T>(string json, Func<IDataContext, T> read, T expected)
    {
        using var clean = Clean(json);
        using var lifted = Lifted(json);
        Assert.Equal(expected, read(clean));
        Assert.Equal(expected, read(lifted));
    }

    private static void AssertBothPathsThrow<T>(string json, Func<IDataContext, T> read)
    {
        using var clean = Clean(json);
        using var lifted = Lifted(json);
        Assert.ThrowsAny<Exception>(() => read(clean));
        Assert.ThrowsAny<Exception>(() => read(lifted));
    }

    [Theory]
    [InlineData("4", 4)]                 // exact integral — built-in fast path, unchanged
    [InlineData("5.0", 5)]               // THE repro
    [InlineData("0.0", 0)]
    [InlineData("5.7", 6)]               // rounds, does not truncate
    [InlineData("5.5", 6)]               // ToEven
    [InlineData("4.5", 4)]               // ToEven — NOT 5
    [InlineData("-4.5", -4)]
    [InlineData("-5.5", -6)]
    [InlineData("1e2", 100)]
    [InlineData("2147483647.4", 2147483647)] // rounds down, just inside Int32
    [InlineData("\"5\"", 5)]             // AllowReadingFromString must survive the custom converter
    [InlineData("\"5.0\"", 5)]           // deliberate extra leniency vs. Newtonsoft
    public void GetInt_CoercesNumber(string literal, int expected)
    {
        AssertBothPaths($$"""{"v":{{literal}}}""", c => c.Get<int>("$.v"), expected);
    }

    [Theory]
    [InlineData("5.0", 5L)]
    [InlineData("5.7", 6L)]
    [InlineData("1e10", 10000000000L)]
    [InlineData("2147483647.6", 2147483648L)] // out of Int32, fine for Int64
    [InlineData("9223372036854775807", 9223372036854775807L)] // exact — must NOT detour via double
    public void GetLong_CoercesNumber(string literal, long expected)
    {
        AssertBothPaths($$"""{"v":{{literal}}}""", c => c.Get<long>("$.v"), expected);
    }

    [Theory]
    [InlineData("2147483647.6")] // rounds up to 2^31
    [InlineData("1e10")]
    [InlineData("-1e10")]
    public void GetInt_OutOfRange_Throws(string literal)
    {
        AssertBothPathsThrow($$"""{"v":{{literal}}}""", c => c.Get<int>("$.v"));
    }

    [Theory]
    [InlineData("1e30")]
    [InlineData("-1e30")]
    public void GetLong_OutOfRange_Throws(string literal)
    {
        AssertBothPathsThrow($$"""{"v":{{literal}}}""", c => c.Get<long>("$.v"));
    }

    [Fact]
    public void GetInt_OnBoolean_StillThrows()
    {
        // Deliberately stricter than Newtonsoft (which yields 1): a bool reaching an Int read is a
        // data bug, not something to coerce silently.
        AssertBothPathsThrow("""{"v":true}""", c => c.Get<int>("$.v"));
    }

    [Fact]
    public void NullAndMissing_Unchanged()
    {
        const string json = """{"v":null}""";
        AssertBothPaths(json, c => c.Get<int?>("$.v"), null);
        AssertBothPaths(json, c => c.Get<int>("$.v"), 0);       // default, never reaches the converter
        AssertBothPaths(json, c => c.Get<int?>("$.absent"), null);
    }

    [Fact]
    public void NullableInt_CoercesThroughNullableFactory()
    {
        // STJ's NullableConverterFactory must pick up the registered JsonConverter<int>.
        AssertBothPaths("""{"v":5.0}""", c => c.Get<int?>("$.v"), 5);
        AssertBothPaths("""{"v":5.0}""", c => c.Get<long?>("$.v"), 5L);
    }

    [Fact]
    public void ClrBackedJsonValue_Coerces()
    {
        // The Math@1 → ConvertDataType@1 shape: a node authored a CLR double, which the parity
        // double converter writes as "2.0" when the node is re-read.
        using var ctx = new DataContextImpl(JsonDocument.Parse("""{"seed":1}""").RootElement);
        ctx.Set("$.x", JsonValue.Create(2.0));
        Assert.Equal(2, ctx.Get<int>("$.x"));
        Assert.Equal(2L, ctx.Get<long>("$.x"));
    }

    [Fact]
    public void TypedArray_CoercesElementwise()
    {
        const string json = """{"arr":[1,2.0,3.7]}""";
        AssertBothPaths(json, c => string.Join(",", c.Get<int[]>("$.arr")!), "1,2,4");
        AssertBothPaths(json, c => string.Join(",", c.GetArray<int>("$.arr")!), "1,2,4");
    }

    [Fact]
    public void DtoMember_Coerces()
    {
        AssertBothPaths("""{"obj":{"X":7.0,"Y":"z"}}""", c => c.Get<Point>("$.obj")!.X, 7);
    }

    [Fact]
    public void TryGet_Coerces()
    {
        using var ctx = Clean("""{"v":5.0}""");
        Assert.True(ctx.TryGet<int>("$.v", out var value));
        Assert.Equal(5, value);
    }

    [Fact]
    public void DynamicBoxingPath_StaysDouble()
    {
        // 🔴 The guard that matters: GetValue() routes through JsonScalar.ToClr, which must keep a
        // real as double. Collapsing it to int here is the BsonInt64 regression the parity double
        // converter exists to prevent — the typed-read coercion must not leak into it.
        using var clean = Clean("""{"v":5.0}""");
        using var lifted = Lifted("""{"v":5.0}""");
        Assert.IsType<double>(clean.GetValue("$.v"));
        Assert.IsType<double>(lifted.GetValue("$.v"));
        Assert.Equal(5.0, clean.GetValue("$.v"));
    }

    [Fact]
    public void WrittenInt_SerializesWithoutTrailingZero()
    {
        // The write side is untouched: an int stays an int on the wire, only doubles get the ".0".
        using var ctx = new DataContextImpl(JsonDocument.Parse("""{"seed":1}""").RootElement);
        ctx.Set("$.i", 5);
        ctx.Set("$.d", 5.0);

        using var ms = new MemoryStream();
        ctx.WriteJsonTo("$", ms);
        var json = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("\"i\":5", json);
        Assert.Contains("\"d\":5.0", json);
    }
}
