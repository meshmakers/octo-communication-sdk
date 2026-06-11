using System.Text.Json;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// Pins the one intentional difference between the SDK pipeline options and the canonical CK-engine
/// Rt serializer: the CK-engine default drops null properties (mirroring RtNewtonsoftSerializer),
/// while the pipeline preserves them so DataKind.Null and DataKind.Undefined stay distinct.
/// </summary>
public class SystemTextJsonOptionsTests
{
    private sealed record Probe(string? Value);

    [Fact]
    public void RtSystemTextJsonSerializer_Default_DropsNulls()
    {
        var json = JsonSerializer.Serialize(new Probe(null), RtSystemTextJsonSerializer.Default);
        Assert.DoesNotContain("Value", json);
    }

    [Fact]
    public void SystemTextJsonOptions_Default_PreservesNulls()
    {
        var json = JsonSerializer.Serialize(new Probe(null), SystemTextJsonOptions.Default);
        Assert.Contains("Value", json);
    }

    [Theory]
    [InlineData("Mühle & Co")]
    [InlineData("Größe <x>")]
    public void RtSystemTextJsonSerializer_Default_EmitsNonAsciiLiterally(string value)
    {
        // The canonical Rt serializer must emit non-ASCII / HTML chars literally (matching the
        // legacy RtNewtonsoftSerializer) rather than STJ's default \uXXXX escaping.
        var json = JsonSerializer.Serialize(new Probe(value), RtSystemTextJsonSerializer.Default);
        Assert.Contains(value, json);
    }

    [Theory]
    [InlineData("Mühle & Co")]
    [InlineData("Größe <x>")]
    public void SystemTextJsonOptions_Default_EmitsNonAsciiLiterally(string value)
    {
        // The pipeline bundle derives from RtSystemTextJsonSerializer, so the relaxed encoder
        // must flow through to it (and to every wire node that uses it).
        var json = JsonSerializer.Serialize(new Probe(value), SystemTextJsonOptions.Default);
        Assert.Contains(value, json);
    }
}
