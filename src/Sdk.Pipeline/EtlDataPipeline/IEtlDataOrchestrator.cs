using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

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
    /// <param name="executionMode">Optional per-execution mode flags surfaced to nodes via
    /// <see cref="Nodes.INodeContext.PipelineExecutionMode"/>. When <c>IsDryRun</c> is true,
    /// Load nodes that honour the flag suppress their real sink and record a "would-have-written"
    /// payload via <see cref="Nodes.INodeContext.RecordDryRunIntent"/>. Null restores classic
    /// (real-effect) behaviour.</param>
    Task<object?> ExecutePipelineAsync<TEtlContext>(NodeDefinitionRoot nodeDefinitionRoot, TEtlContext etlContext,
        IPipelineDebugger? pipelineDebugger = null, object? value = null,
        IPipelineExecutionMode? executionMode = null) where TEtlContext : class, IEtlContext;
}