using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Newtonsoft-oracle parity for <see cref="JsonScalar.ToClr(JsonValue, bool)"/> on CLR-backed
/// JsonValues (JsonValue.Create(primitive) — what pipeline nodes author). The boxed CLR type must
/// match Newtonsoft's in-memory round-trip oracle (JObject.FromObject(...).Value): string→string,
/// int→Int32, long→Int64, real→double, bool→bool. Pins the exact-type contract so a future change
/// can't silently collapse double→int (the BsonInt64 regression class).
/// </summary>
public class ClrBackedScalarParityTests
{
    private static object? NewtonsoftBoxed(object value) =>
        ((JValue)JObject.FromObject(new { v = value })["v"]!).Value;

    [Fact]
    public void ClrBackedString_MatchesNewtonsoft()
    {
        Assert.Equal(NewtonsoftBoxed("DAR"), JsonScalar.ToClr(JsonValue.Create("DAR")!, parseDateStrings: false));
        Assert.IsType<string>(JsonScalar.ToClr(JsonValue.Create("DAR")!, parseDateStrings: false));
    }

    [Fact]
    public void ClrBackedSmallInt_MatchesNewtonsoft_Int32()
    {
        // Newtonsoft's FromObject round-trip keeps Int32 (not Int64) — JsonScalar's "prefer Int32".
        var nt = NewtonsoftBoxed(42);
        var stj = JsonScalar.ToClr(JsonValue.Create(42)!);
        Assert.IsType<int>(nt);
        Assert.IsType<int>(stj);
        Assert.Equal(42, stj);
    }

    [Fact]
    public void ClrBackedLargeInt_ReturnsLong()
    {
        var stj = JsonScalar.ToClr(JsonValue.Create(2147483648L)!);
        Assert.IsType<long>(stj);
        Assert.Equal(2147483648L, stj);
    }

    [Fact]
    public void ClrBackedDouble_MatchesNewtonsoft_StaysDouble()
    {
        var nt = NewtonsoftBoxed(2.0);
        var stj = JsonScalar.ToClr(JsonValue.Create(2.0)!);
        Assert.IsType<double>(nt);
        Assert.IsType<double>(stj);  // NOT int — the crux
        Assert.Equal(2.0, stj);
    }

    [Fact]
    public void ClrBackedBool_MatchesNewtonsoft()
    {
        Assert.Equal(NewtonsoftBoxed(true), JsonScalar.ToClr(JsonValue.Create(true)!));
        Assert.IsType<bool>(JsonScalar.ToClr(JsonValue.Create(true)!));
    }

    [Fact]
    public void ClrBackedIsoString_ParseDatesTrue_ReturnsDateTime()
    {
        var stj = JsonScalar.ToClr(JsonValue.Create("2024-01-15T10:30:00")!, parseDateStrings: true);
        Assert.IsType<DateTime>(stj);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), stj);
    }
}
