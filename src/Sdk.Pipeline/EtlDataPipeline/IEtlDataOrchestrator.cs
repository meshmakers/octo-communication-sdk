using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Interface for an extract-transform-load data orchestrator
/// </summary>
public interface IEtlDataOrchestrator
{
    /// <summary>
    /// Executes the pipeline
    /// </summary>
    /// <param name="nodeDefinitionRoot">Configuration of the data pipeline to run</param>
    /// <param name="etlContext">Context the data pipeline is running in to pass information about tenants, adapters etc.</param>
    /// <param name="pipelineDebugger">An optional pipeline debugger</param>
    /// <param name="value">An optional value to pass to the pipeline</param>
    Task<object?> ExecutePipelineAsync<TEtlContext>(NodeDefinitionRoot nodeDefinitionRoot, TEtlContext etlContext,
        IPipelineDebugger? pipelineDebugger = null, object? value = null) where TEtlContext : class, IEtlContext;
}