using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Implementation of <see cref="IPipelineExecutionReporter"/> that reports execution metrics
/// via the adapter hub client to the communication controller.
/// </summary>
public class AdapterPipelineExecutionReporter : IPipelineExecutionReporter
{
    private readonly IAdapterHubClient _adapterHubClient;
    private readonly ILogger<AdapterPipelineExecutionReporter> _logger;

    /// <summary>
    /// Creates a new instance of the reporter.
    /// </summary>
    /// <param name="adapterHubClient">The adapter hub client for communication with the controller</param>
    /// <param name="logger">Logger instance</param>
    public AdapterPipelineExecutionReporter(IAdapterHubClient adapterHubClient, ILogger<AdapterPipelineExecutionReporter> logger)
    {
        _adapterHubClient = adapterHubClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ReportExecutionStartAsync(
        RtEntityId pipelineRtEntityId,
        Guid executionId,
        PipelineTriggerType triggerType,
        DateTime startedAt,
        string? inputData = null)
    {
        try
        {
            var startDto = new PipelineExecutionStartDto
            {
                ExecutionId = executionId.ToString(),
                PipelineRtEntityId = pipelineRtEntityId,
                TriggerType = triggerType,
                StartedAt = startedAt,
                InputData = inputData
            };

            await _adapterHubClient.ReportExecutionStartAsync(startDto);
            _logger.LogDebug("Reported execution start for pipeline {PipelineId}, execution {ExecutionId}",
                pipelineRtEntityId, executionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report execution start for pipeline {PipelineId}, execution {ExecutionId}",
                pipelineRtEntityId, executionId);
            // Don't throw - reporting failures shouldn't stop pipeline execution
        }
    }

    /// <inheritdoc />
    public async Task ReportExecutionEndAsync(
        Guid executionId,
        PipelineExecutionStatus status,
        DateTime completedAt,
        int durationMs,
        string? errorMessage = null,
        string? outputData = null)
    {
        try
        {
            var endDto = new PipelineExecutionEndDto
            {
                ExecutionId = executionId.ToString(),
                Status = status,
                CompletedAt = completedAt,
                DurationMs = durationMs,
                ErrorMessage = errorMessage,
                OutputData = outputData
            };

            await _adapterHubClient.ReportExecutionEndAsync(endDto);
            _logger.LogDebug("Reported execution end for execution {ExecutionId} with status {Status}",
                executionId, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report execution end for execution {ExecutionId}",
                executionId);
            // Don't throw - reporting failures shouldn't stop pipeline execution
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetInterruptedExecutionIdsAsync()
    {
        try
        {
            return await _adapterHubClient.GetInterruptedExecutionIdsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get interrupted execution IDs");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task ReportInterruptedExecutionResultAsync(
        Guid executionId,
        PipelineExecutionStatus status,
        DateTime completedAt,
        int durationMs,
        string? errorMessage = null,
        string? outputData = null)
    {
        try
        {
            var endDto = new PipelineExecutionEndDto
            {
                ExecutionId = executionId.ToString(),
                Status = status,
                CompletedAt = completedAt,
                DurationMs = durationMs,
                ErrorMessage = errorMessage,
                OutputData = outputData
            };

            await _adapterHubClient.ReportInterruptedExecutionResultAsync(endDto);
            _logger.LogDebug("Reported interrupted execution result for execution {ExecutionId} with status {Status}",
                executionId, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report interrupted execution result for execution {ExecutionId}",
                executionId);
        }
    }
}
