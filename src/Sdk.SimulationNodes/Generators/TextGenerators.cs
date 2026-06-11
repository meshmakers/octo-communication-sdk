using System.Text.Json.Nodes;
using Bogus;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Generators;

internal class WordLoremGenerator : IValueGenerator
{
    public object Generate(IEtlContext etlContext, Faker faker, JsonObject configuration)
    {
        var wordCount = configuration.GetValue("count", 1);

        var words = faker.Lorem.Words(wordCount);
        return string.Join(" ", words);
    }
}
