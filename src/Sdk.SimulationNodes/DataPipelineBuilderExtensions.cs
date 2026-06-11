using Bogus;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;
using Meshmakers.Octo.Sdk.SimulationNodes.Generators;
using Meshmakers.Octo.Sdk.SimulationNodes.Nodes.Extracts;
using Meshmakers.Octo.Sdk.SimulationNodes.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.SimulationNodes;

/// <summary>
/// Extensions for simulation pipeline nodes
/// </summary>
public static class DataPipelineBuilderExtensions
{
    /// <summary>
    /// Adds simulation nodes for pipeline
    /// </summary>
    /// <param name="pipelineBuilder"></param>
    /// <returns></returns>
    public static IDataPipelineBuilder AddSimulationNodes(this IDataPipelineBuilder pipelineBuilder)
    {
        pipelineBuilder.RegisterNodeConfiguration<SimulationNodeConfiguration>();
        pipelineBuilder.RegisterNode<SimulationNode>();

        Randomizer.Seed = new Random(8644909);

        pipelineBuilder.Services.AddSingleton<ISimulationService, SimulationService>();

        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, SinusGenerator>("Math.Sinus");
        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, TriangleGenerator>("Math.Triangle");
        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, ConstantGenerator>("Math.Constant");
        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, IntRandomGenerator>("Math.IntRandom");
        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, DoubleRandomGenerator>("Math.DoubleRandom");

        pipelineBuilder.Services.AddKeyedTransient<IValueGenerator, WordLoremGenerator>("Text.Lorem.Word");

        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, SteppedDateTimeGenerator>("Energy.SteppedDateTime");
        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, LoadProfileGenerator>("Energy.LoadProfile");
        pipelineBuilder.Services.AddKeyedSingleton<IValueGenerator, PvProfileGenerator>("Energy.PvProfile");

        return pipelineBuilder;
    }
}
