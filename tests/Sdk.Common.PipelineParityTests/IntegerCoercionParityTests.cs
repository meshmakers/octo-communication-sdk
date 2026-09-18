using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Newtonsoft-oracle parity for typed integer reads — <c>DataContextImpl.Get&lt;int&gt;</c> /
/// <c>Get&lt;long&gt;</c> against <c>JToken.ToObject&lt;int&gt;</c> / <c>&lt;long&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This suite is what DECIDES the coercion semantics of
/// <see cref="NewtonsoftParityInt32Converter"/> / <see cref="NewtonsoftParityInt64Converter"/>; the
/// converters were written to whatever the oracle does, not the other way round. STJ's built-in
/// Int32 converter rejects the literal <c>5.0</c> on its raw text, which broke every pipeline that
/// wrote a double (nodes emit a trailing <c>.0</c> for integral doubles by design) and read it back
/// as Int (AB#5275).
/// </para>
/// <para>
/// The oracle is consulted at runtime rather than hard-coded, so the assertion cannot drift from
/// Newtonsoft: each case asserts either "both produce the same value" or "both reject it".
/// </para>
/// </remarks>
public class IntegerCoercionParityTests
{
    /// <summary>
    /// Literals where the oracle succeeds for BOTH Int32 and Int64. The expected value is taken
    /// from Newtonsoft at runtime — the inline comments only record what it produced when the
    /// suite was written, as a reading aid.
    /// </summary>
    [Theory]
    [InlineData("4")]      // exact integral — the built-in fast path
    [InlineData("5.0")]    // 5   — THE repro: integral value, fractional raw text
    [InlineData("0.0")]    // 0
    [InlineData("5.7")]    // 6   — rounds, does not truncate
    [InlineData("5.5")]    // 6   — ToEven
    [InlineData("4.5")]    // 4   — ToEven (NOT 5)
    [InlineData("-4.5")]   // -4  — ToEven
    [InlineData("-5.5")]   // -6  — ToEven
    [InlineData("-5.7")]   // -6
    [InlineData("5.4999999")] // 5
    [InlineData("1e2")]    // 100 — exponent notation
    [InlineData("2147483647.4")] // int.MaxValue, rounds down into range
    [InlineData("\"5\"")]  // 5   — AllowReadingFromString
    public void IntegerLiteral_MatchesNewtonsoftOracle(string literal)
    {
        var json = $$"""{"v":{{literal}}}""";

        var oracle32 = Oracle<int>(json);
        var oracle64 = Oracle<long>(json);
        Assert.True(oracle32.Succeeded, $"Oracle unexpectedly rejected Int32 for {literal}");
        Assert.True(oracle64.Succeeded, $"Oracle unexpectedly rejected Int64 for {literal}");

        using var ctx = new DataContextImpl(JsonDocument.Parse(json));
        Assert.Equal(oracle32.Value, ctx.Get<int>("$.v"));
        Assert.Equal(oracle64.Value, ctx.Get<long>("$.v"));
    }

    /// <summary>
    /// Literals outside Int32 but inside Int64: the oracle rejects the narrow read and accepts the
    /// wide one, and so must we. Pins that the Int64 path does not inherit the Int32 bound.
    /// </summary>
    [Theory]
    [InlineData("2147483647.6")] // rounds up to 2^31 — just out of Int32
    [InlineData("1e10")]
    [InlineData("-1e10")]
    [InlineData("9223372036854775807")] // long.MaxValue, exact — must NOT detour through double
    public void OutOfInt32Range_RejectedByBoth_ButReadableAsInt64(string literal)
    {
        var json = $$"""{"v":{{literal}}}""";

        Assert.False(Oracle<int>(json).Succeeded, $"Oracle unexpectedly accepted Int32 for {literal}");
        var oracle64 = Oracle<long>(json);
        Assert.True(oracle64.Succeeded, $"Oracle unexpectedly rejected Int64 for {literal}");

        using var ctx = new DataContextImpl(JsonDocument.Parse(json));
        Assert.ThrowsAny<Exception>(() => ctx.Get<int>("$.v"));
        Assert.Equal(oracle64.Value, ctx.Get<long>("$.v"));
    }

    /// <summary>
    /// Beyond Int64 both must reject. The exception TYPE is deliberately not pinned: Newtonsoft
    /// throws <see cref="OverflowException"/>, the converters throw <see cref="JsonException"/> so
    /// that <c>DateTimeNode</c>'s <c>catch (JsonException)</c> keeps working.
    /// </summary>
    [Theory]
    [InlineData("1e30")]
    [InlineData("-1e30")]
    public void OutOfInt64Range_RejectedByBoth(string literal)
    {
        var json = $$"""{"v":{{literal}}}""";

        Assert.False(Oracle<int>(json).Succeeded);
        Assert.False(Oracle<long>(json).Succeeded);

        using var ctx = new DataContextImpl(JsonDocument.Parse(json));
        Assert.ThrowsAny<Exception>(() => ctx.Get<int>("$.v"));
        Assert.ThrowsAny<Exception>(() => ctx.Get<long>("$.v"));
    }

    /// <summary>
    /// DELIBERATE DIVERGENCE 1 — a quoted real. Newtonsoft's string path is integer-only and throws
    /// a FormatException; the converters fall back to a real parse so the quoted and unquoted forms
    /// behave alike (REST sources that stringify numbers hit exactly this).
    /// </summary>
    [Theory]
    [InlineData("\"5.0\"", 5)]
    [InlineData("\"5.7\"", 6)]
    public void QuotedReal_DivergesFromOracle_ByDesign(string literal, int expected)
    {
        var json = $$"""{"v":{{literal}}}""";

        Assert.False(Oracle<int>(json).Succeeded, "Oracle behaviour changed — revisit the divergence");

        using var ctx = new DataContextImpl(JsonDocument.Parse(json));
        Assert.Equal(expected, ctx.Get<int>("$.v"));
        Assert.Equal(expected, ctx.Get<long>("$.v"));
    }

    /// <summary>
    /// DELIBERATE DIVERGENCE 2 — a JSON boolean. Newtonsoft coerces <c>true</c> to 1; the converters
    /// reject it, because silently turning a bool into a number hides a real data bug and no
    /// pipeline should depend on it.
    /// </summary>
    [Fact]
    public void Boolean_DivergesFromOracle_ByDesign()
    {
        const string json = """{"v":true}""";

        var oracle = Oracle<int>(json);
        Assert.True(oracle.Succeeded, "Oracle behaviour changed — revisit the divergence");
        Assert.Equal(1, oracle.Value);

        using var ctx = new DataContextImpl(JsonDocument.Parse(json));
        Assert.ThrowsAny<Exception>(() => ctx.Get<int>("$.v"));
        Assert.ThrowsAny<Exception>(() => ctx.Get<long>("$.v"));
    }

    private static (bool Succeeded, T Value) Oracle<T>(string json)
    {
        try
        {
            return (true, JObject.Parse(json)["v"]!.ToObject<T>()!);
        }
        catch (Exception)
        {
            return (false, default!);
        }
    }
}
