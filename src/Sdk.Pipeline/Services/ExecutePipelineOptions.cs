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
}