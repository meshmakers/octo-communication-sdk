using System.Text.Json.Nodes;
using Bogus;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Generators;

internal interface IValueGenerator
{
    object? Generate(IEtlContext etlContext, Faker faker, JsonObject configuration);
}
