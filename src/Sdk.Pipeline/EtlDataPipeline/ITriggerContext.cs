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

    /// <summary>
    /// Reports the pipeline's live status line — what this trigger last did — to the communication
    /// controller, which writes it to the pipeline entity's <c>StatusMessage</c> (AB#5385). Meant to
    /// be called after every poll and from the poll loop's failure path, so an operator sees the
    /// last outcome on the pipeline instead of a "Deployed" that has been failing for days.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget for the caller: never throws and never blocks the poll on the controller.
    /// A failure to deliver (hub down, a controller predating the method) is logged at Debug and
    /// rate-limited, not surfaced — the next poll replaces the line anyway. Keep the line short
    /// (the controller truncates at 1000 characters) and never include credentials or message
    /// bodies in it.
    /// </remarks>
    /// <param name="message">One status line, e.g. an ISO-8601 UTC timestamp, the source polled and the counts</param>
    /// <param name="isError">True when the line reports a failed poll</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReportStatusAsync(string message, bool isError = false, CancellationToken cancellationToken = default);
}