using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// End-to-end reproduction of the handle-ec-podlist-process.yaml failure: a node authors a scalar
/// via <c>Set(path, JsonValue.Create(...))</c> (a CLR-backed JsonValue), which lifts the overlay;
/// reading it back via <c>GetValue</c> routed through <c>ElementSource.GetValue</c> →
/// <c>JsonScalar.ToClr(JsonValue)</c>, which previously threw
/// "A value of type 'System.String' cannot be converted to a 'System.Text.Json.JsonElement'".
/// </summary>
public class DataContextClrBackedScalarTests
{
    [Fact]
    public void GetValue_AfterSetClrBackedString_ReturnsString()
    {
        // TransformStringNode does exactly this: matchCtx.Set(targetPath, JsonValue.Create(result)).
        using var doc = JsonDocument.Parse("""{"Operator":"original"}""");
        var dc = new DataContextImpl(doc);

        dc.Set("$.Operator", JsonValue.Create("DAR"));

        Assert.Equal("DAR", dc.GetValue("$.Operator", parseDateStrings: false));
    }

    [Fact]
    public void GetValue_AfterSetClrBackedDouble_StaysDouble()
    {
        // The datatype-regression guard: a written double must read back as double, not collapse to int.
        using var doc = JsonDocument.Parse("""{"x":0}""");
        var dc = new DataContextImpl(doc);

        dc.Set("$.x", JsonValue.Create(2.0));

        var v = dc.GetValue("$.x");
        Assert.IsType<double>(v);
        Assert.Equal(2.0, v);
    }

    [Fact]
    public void GetValue_AfterSetClrBackedInt_ReturnsInt()
    {
        using var doc = JsonDocument.Parse("""{"x":0}""");
        var dc = new DataContextImpl(doc);

        dc.Set("$.x", JsonValue.Create(42));

        var v = dc.GetValue("$.x");
        Assert.IsType<int>(v);
        Assert.Equal(42, v);
    }

    [Fact]
    public void GetValue_AfterSetRawString_ReturnsString()
    {
        // Set<string>(rawString) goes through SerializeToNode; also must round-trip via GetValue.
        using var doc = JsonDocument.Parse("""{"Operator":"original"}""");
        var dc = new DataContextImpl(doc);

        dc.Set("$.Operator", "DAR");

        Assert.Equal("DAR", dc.GetValue("$.Operator", parseDateStrings: false));
    }
}
