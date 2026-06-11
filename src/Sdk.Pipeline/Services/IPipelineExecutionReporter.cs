using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Interface for reporting pipeline execution metrics to the communication controller.
/// Implementations should handle the communication with the backend service.
/// </summary>
public interface IPipelineExecutionReporter
{
    /// <summary>
    /// Reports the start of a pipeline execution.
    /// </summary>
    /// <param name="pipelineRtEntityId">The pipeline entity ID</param>
    /// <param name="executionId">Unique execution identifier</param>
    /// <param name="triggerType">Type of trigger that started the execution</param>
    /// <param name="startedAt">When the execution started (UTC)</param>
    /// <param name="inputData">Optional input data for debugging</param>
    /// <returns>Task representing the async operation</returns>
    Task ReportExecutionStartAsync(
        RtEntityId pipelineRtEntityId,
        Guid executionId,
        PipelineTriggerType triggerType,
        DateTime startedAt,
        string? inputData = null);

    /// <summary>
    /// Reports the end of a pipeline execution.
    /// </summary>
    /// <param name="executionId">Unique execution identifier</param>
    /// <param name="status">Final status of the execution</param>
    /// <param name="completedAt">When the execution completed (UTC)</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="errorMessage">Optional error message if failed</param>
    /// <param name="outputData">Optional output data (JSON) from pipeline result</param>
    /// <returns>Task representing the async operation</returns>
    Task ReportExecutionEndAsync(
        Guid executionId,
        PipelineExecutionStatus status,
        DateTime completedAt,
        int durationMs,
        string? errorMessage = null,
        string? outputData = null);

    /// <summary>
    /// Gets the list of execution IDs that were marked as interrupted when this adapter disconnected.
    /// Called after reconnection to determine which executions need their final status reported.
    /// </summary>
    /// <returns>List of interrupted execution IDs</returns>
    Task<IReadOnlyList<string>> GetInterruptedExecutionIdsAsync();

    /// <summary>
    /// Reports the final result of an execution that was previously marked as interrupted.
    /// </summary>
    /// <param name="executionId">Unique execution identifier</param>
    /// <param name="status">Final status of the execution</param>
    /// <param name="completedAt">When the execution completed (UTC)</param>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="errorMessage">Optional error message if failed</param>
    /// <param name="outputData">Optional output data (JSON) from pipeline result</param>
    /// <returns>Task representing the async operation</returns>
    Task ReportInterruptedExecutionResultAsync(
        Guid executionId,
        PipelineExecutionStatus status,
        DateTime completedAt,
        int durationMs,
        string? errorMessage = null,
        string? outputData = null);
}
