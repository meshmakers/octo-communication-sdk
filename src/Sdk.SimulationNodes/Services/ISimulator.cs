using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Services;

/// <summary>
/// Simulator interface to generate values for simulation.
/// </summary>
public interface ISimulator
{
    /// <summary>
    /// Generate a value using the specified generator.
    /// </summary>
    /// <param name="simulatorKey"></param>
    /// <param name="etlContext"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    object? Generate(string simulatorKey, IEtlContext etlContext, JsonObject config);
}
