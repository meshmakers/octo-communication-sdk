using System.Globalization;
using Bogus;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.Services;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Generators;

/// <summary>
/// Returns <c>startDate + index * stepSize</c> as a UTC DateTime. Deterministic — used to
/// emit equally-spaced slot boundaries (e.g. 15-min EDA windows) inside a <c>For@1</c> loop
/// where <c>index</c> comes from the loop iterator.
/// </summary>
internal class SteppedDateTimeGenerator : IValueGenerator
{
    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var startDateString = configuration.GetValue("startDate", DateTime.UtcNow.ToString("o"));
        var stepSizeString = configuration.GetValue("stepSize", "PT15M");
        var index = configuration.GetValue<int>("index", 0);

        var startDate = DateTime.Parse(startDateString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var step = System.Xml.XmlConvert.ToTimeSpan(stepSizeString);

        return DateTime.SpecifyKind(startDate.Add(TimeSpan.FromTicks(step.Ticks * index)), DateTimeKind.Utc);
    }
}

/// <summary>
/// Returns the energy (in kWh) for a 15-min slot of a German BDEW standard load profile.
/// Configuration: <c>profile</c> (H0/G0/L0; default H0), <c>dailyEnergyKwh</c> (total daily
/// energy in kWh; default 10), <c>slotIndex</c> (0..95; default 0). The math is provided by
/// <see cref="EnergyProfiles.LoadProfileSlot"/>.
/// </summary>
internal class LoadProfileGenerator : IValueGenerator
{
    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var profile = configuration.GetValue("profile", "H0");
        var dailyEnergyKwh = configuration.GetValue("dailyEnergyKwh", 10.0);
        var slotIndex = configuration.GetValue<int>("slotIndex", 0);

        try
        {
            return EnergyProfiles.LoadProfileSlot(profile, dailyEnergyKwh, slotIndex);
        }
        catch (ArgumentException ex)
        {
            throw new PipelineNodeExecutionException(ex.Message);
        }
    }
}

/// <summary>
/// Returns the PV-production energy (in kWh) for a 15-min slot.
/// Configuration: <c>peakKwp</c> (default 5), <c>dayOfYear</c> (1..366; default 172),
/// <c>slotIndex</c> (0..95). The math is provided by <see cref="EnergyProfiles.PvProfileSlot"/>.
/// </summary>
internal class PvProfileGenerator : IValueGenerator
{
    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var peakKwp = configuration.GetValue("peakKwp", 5.0);
        var dayOfYear = configuration.GetValue<int>("dayOfYear", 172);
        var slotIndex = configuration.GetValue<int>("slotIndex", 0);

        try
        {
            return EnergyProfiles.PvProfileSlot(peakKwp, dayOfYear, slotIndex);
        }
        catch (ArgumentException ex)
        {
            throw new PipelineNodeExecutionException(ex.Message);
        }
    }
}
