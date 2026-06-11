using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// Pins how a plain CLR enum (e.g. the EDA adapter's EnergyDirection on MeteringPointUpdateInfo)
/// is represented when a node writes a typed object graph into the DataContext via Set&lt;T&gt;.
/// The pipeline serializer (SystemTextJsonOptions, derived from RtSystemTextJsonSerializer) must
/// emit the enum as its underlying INTEGER — Newtonsoft parity — so downstream consumers that read
/// it as Int (DataMapping@1 sourceValueType: Int, If@1/Switch@1 valueType: Int) match. The reader
/// stays tolerant of the member-name string form (Newtonsoft read parity). Guards against the
/// JsonStringEnumConverter regression that emitted the name.
/// </summary>
public class EnumDataContextRepresentationTests
{
    private enum SampleDirection
    {
        Unknown = 0,
        Consumption = 1,
        Production = 2
    }

    [Fact]
    public void Set_ClrEnum_AppearsInDataContextAsInteger()
    {
        var ctx = new DataContextImpl(JsonDocument.Parse("{}"));

        ctx.Set("$.Direction", SampleDirection.Consumption);

        // Newtonsoft parity: the enum is a JSON number (its underlying integer), not the member name.
        Assert.Equal(DataKind.Number, ctx.GetKind("$.Direction"));
        Assert.Equal(1, ctx.Get<int>("$.Direction"));
        Assert.Equal(SampleDirection.Consumption, ctx.Get<SampleDirection>("$.Direction"));
        Assert.NotEqual(DataKind.String, ctx.GetKind("$.Direction"));
    }

    [Fact]
    public void Get_ClrEnum_TolerantOfNameStringForm()
    {
        // Read parity: a name-form value (e.g. externally-supplied data) still deserializes to the enum.
        var ctx = new DataContextImpl(JsonDocument.Parse("""{ "Direction": "Production" }"""));

        Assert.Equal(SampleDirection.Production, ctx.Get<SampleDirection>("$.Direction"));
    }
}
