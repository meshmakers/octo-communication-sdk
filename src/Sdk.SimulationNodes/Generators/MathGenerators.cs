using System.Text.Json.Nodes;
using Bogus;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Generators;

internal class IntRandomGenerator : IValueGenerator
{
    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var min = configuration.GetValue("min", 1);
        var max = configuration.GetValue("max", 100);
        return faker.Random.Int(min, max);
    }
}

internal class DoubleRandomGenerator : IValueGenerator
{
    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var min = configuration.GetValue("min", 1.0);
        var max = configuration.GetValue("max", 100.0);
        return faker.Random.Double(min, max);
    }
}

internal class SinusGenerator : IValueGenerator
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var amplitude = configuration.GetValue("amplitude", 1);
        var frequency = configuration.GetValue("frequency", 1);

        double value = amplitude * Math.Sin(2 * Math.PI * frequency * (DateTime.UtcNow - StartTime).TotalSeconds);
        return value;
    }
}

internal class TriangleGenerator : IValueGenerator
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var amplitude = configuration.GetValue("amplitude", 1);
        double frequency = configuration.GetValue("frequency", 1);

        double slope = 4 * amplitude / frequency;

        double elapsed = (DateTime.UtcNow - StartTime).TotalSeconds % frequency;
        double value = elapsed < frequency / 2
            ? slope * elapsed - amplitude
            : -slope * elapsed + 3 * amplitude;
        return value;
    }
}

internal class ConstantGenerator : IValueGenerator
{
    public object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var amplitude = 1;
        if (configuration.TryGetPropertyValue("amplitude", out var amplitudeNode) && amplitudeNode is not null)
        {
            amplitude = amplitudeNode.GetValue<int>();
        }

        return amplitude;
    }
}
