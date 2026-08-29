using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Options for executing a pipeline
/// </summary>
public class ExecutePipelineOptions(DateTime transactionStartedDateTime)
{
    /// <summary>
    /// Gets or sets the date and time when the transaction started
    /// </summary>
    public DateTime TransactionStartedDateTime { get; } = transactionStartedDateTime;

    /// <summary>
    /// Gets or sets the date and time when the transaction started
    /// </summary>
    public DateTime? ExternalReceivedDateTime { get; set; }

    /// <summary>
    /// Gets or sets the trigger type that initiated this execution
    /// </summary>
    public PipelineTriggerType TriggerType { get; set; } = PipelineTriggerType.Event;

    /// <summary>
    /// Gets or sets optional input data for debugging (will be truncated if too long)
    /// </summary>
    public string? InputData { get; set; }

    /// <summary>
    /// When true, the orchestrator runs the pipeline with
    /// <c>IPipelineExecutionMode.IsDryRun=true</c> so retrofitted Load nodes
    /// suppress their real sink and record a "would-have-written" intent on
    /// the debug stream (M4-B.2). Default false preserves classic semantics.
    /// </summary>
    public bool IsDryRun { get; set; }

    /// <summary>
    /// Gets or sets the authenticated caller of the trigger, verified by the trigger's own
    /// authorization (AB#4975). Null for anonymous and internal triggers.
    /// </summary>
    public VerifiedPrincipal? VerifiedPrincipal { get; set; }
}