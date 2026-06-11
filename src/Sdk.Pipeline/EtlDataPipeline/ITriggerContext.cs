using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Interface for extract node context
/// </summary>
public interface ITriggerContext
{
    /// <summary>
    /// Returns the tenant id of the pipeline
    /// </summary>
    string TenantId { get; }
    
    /// <summary>
    /// Runtime id of the data flow
    /// </summary>
    OctoObjectId DataFlowRtId { get; }
    
    /// <summary>
    /// Returns the pipeline runtime id
    /// </summary>
    RtEntityId PipelineRtEntityId { get; }
    
    /// <summary>
    /// Returns the node context, that contains information about the current node
    /// </summary>
    INodeContext NodeContext { get; }
    
    /// <summary>
    /// Returns the global configuration for the pipeline
    /// </summary>
    IGlobalConfiguration GlobalConfiguration { get; }
    
    /// <summary>
    /// Triggers the execution of the recent transformation pipeline
    /// </summary>
    /// <returns></returns>
    Task<object?> ExecuteAsync(ExecutePipelineOptions executePipelineOptions, object? input = null);
    
    /// <summary>
    /// Starts the execution of a pipeline
    /// </summary>
    /// <param name="executePipelineOptions">Options for executing the pipeline</param>
    /// <param name="value">Input value that is passed to the first node of the pipeline</param>
    /// <returns>The pipeline execution id that is unique per execution</returns>
    Task<Guid> StartExecutePipelineAsync(ExecutePipelineOptions executePipelineOptions, object? value = null);
    
    /// <summary>
    /// Ends the execution of a pipeline
    /// </summary>

    /// <param name="pipelineExecutionId">The pipeline execution id that is unique per execution</param>
    /// <returns>The result of the pipeline execution</returns>
    Task<object?> EndExecutePipelineAsync(Guid pipelineExecutionId);
}