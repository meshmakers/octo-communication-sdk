using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.SimulationNodes.Services;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Nodes.Extracts;

/// <summary>
/// Configuration for the simulation node
/// </summary>
[NodeName("Simulation", 1)]
public record SimulationNodeConfiguration : NodeConfiguration
{
    /// <summary>
    /// Required locale for the simulation
    /// </summary>
    public required string Locale { get; init; } = "en";

    /// <summary>
    /// List of transformations to apply to the signal
    /// </summary>
    public required ICollection<SimulationPropertyConfiguration>? Simulations { get; init; }
}

/// <summary>
/// Configuration for a single simulation property
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class SimulationPropertyConfiguration
{
    /// <summary>
    /// Target path to set the value to
    /// </summary>
    public string TargetPath { get; set; } = null!;

    /// <summary>
    /// Kind of simulator to use
    /// </summary>
    public string SimulatorKey { get; set; } = "Increment";

    /// <summary>
    /// Configuration for the simulator
    /// </summary>
    public string Configuration { get; set; } = "{}";
}

/// <summary>
/// Generates a value for a target path using a simulator
/// </summary>
/// <param name="next">Next node in the pipeline</param>
/// <param name="etlContext">The ETL context</param>
/// <param name="simulationService">Simulation service</param>
[NodeConfiguration(typeof(SimulationNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class SimulationNode(NodeDelegate next, IEtlContext etlContext, ISimulationService simulationService)
    : IPipelineNode
{
    /// <inheritdoc />
    public Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<SimulationNodeConfiguration>();

        if (c.Simulations != null)
        {
            var simulator = simulationService.GetSimulator(c.Locale);

            foreach (var simulation in c.Simulations)
            {
                var config = JsonNode.Parse(simulation.Configuration) as JsonObject ?? new JsonObject();
                var value = simulator.Generate(simulation.SimulatorKey, etlContext, config);
                if (value == null)
                {
                    dataContext.Set<object>(simulation.TargetPath, null,
                        DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Overwrite);
                }
                else
                {
                    dataContext.Set(simulation.TargetPath, value,
                        DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Overwrite);
                }
            }
        }

        return next(dataContext, nodeContext);
    }
}
