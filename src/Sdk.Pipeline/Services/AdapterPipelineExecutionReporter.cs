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
    /// <summary>
    /// How long a status-line delivery failure silences the next ones (AB#5385). A trigger reports
    /// once per poll, and against a controller that predates the hub method or is down every one
    /// of those fails; one Debug line per window is diagnostic, one per poll is a log flood.
    /// </summary>
    public static readonly TimeSpan StatusFailureLogInterval = TimeSpan.FromMinutes(5);

    private readonly IAdapterHubClient _adapterHubClient;
    private readonly ILogger<AdapterPipelineExecutionReporter> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _statusFailureGate = new();
    private DateTimeOffset _statusFailureLogSuppressedUntil = DateTimeOffset.MinValue;
    private int _statusFailuresSuppressed;

    /// <summary>
    /// Creates a new instance of the reporter.
    /// </summary>
    /// <param name="adapterHubClient">The adapter hub client for communication with the controller</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timeProvider">Clock behind the status-failure log rate limit; the system clock when omitted</param>
    public AdapterPipelineExecutionReporter(IAdapterHubClient adapterHubClient,
        ILogger<AdapterPipelineExecutionReporter> logger, TimeProvider? timeProvider = null)
    {
        _adapterHubClient = adapterHubClient;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task ReportPipelineStatusAsync(RtEntityId pipelineRtEntityId, string message, bool isError,
        DateTime timestampUtc)
    {
        try
        {
            var status = new PipelineStatusReportDto
            {
                PipelineRtEntityId = pipelineRtEntityId,
                Message = message,
                IsError = isError,
                TimestampUtc = timestampUtc
            };

            await _adapterHubClient.ReportPipelineStatusAsync(status);
            _logger.LogTrace("Reported status for pipeline {PipelineId}: {Message}", pipelineRtEntityId, message);
        }
        catch (Exception ex)
        {
            // Never thrown, never above Debug: the line is informational and the next poll replaces
            // it. The SDK client sends it fire-and-forget, so what lands here is a connection that is
            // not active (InvalidOperationException) or a transport failure — a controller without the
            // method drops the message on its side without an error reaching the adapter. Either way
            // this repeats on every poll, hence the rate limit.
            LogStatusFailureRateLimited(ex, pipelineRtEntityId);
        }
    }

    private void LogStatusFailureRateLimited(Exception ex, RtEntityId pipelineRtEntityId)
    {
        int suppressed;
        lock (_statusFailureGate)
        {
            var now = _timeProvider.GetUtcNow();
            if (now < _statusFailureLogSuppressedUntil)
            {
                _statusFailuresSuppressed++;
                return;
            }

            suppressed = _statusFailuresSuppressed;
            _statusFailuresSuppressed = 0;
            _statusFailureLogSuppressedUntil = now + StatusFailureLogInterval;
        }

        _logger.LogDebug(ex,
            "Failed to report the status line for pipeline {PipelineId} to the communication controller " +
            "({Suppressed} earlier failure(s) not logged); further failures are not logged for {Interval}. " +
            "A controller predating AB#5385 does not accept the status line — the pipeline's StatusMessage stays as it is.",
            pipelineRtEntityId, suppressed, StatusFailureLogInterval);
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
    public async Task<int> FailOrphanedExecutionsAsync(DateTime processStartUtc)
    {
        try
        {
            var failedCount = await _adapterHubClient.FailOrphanedExecutionsAsync(processStartUtc);
            if (failedCount > 0)
            {
                _logger.LogInformation(
                    "Controller failed {Count} execution(s) orphaned by the previous adapter process (started before {ProcessStartUtc})",
                    failedCount, processStartUtc);
            }

            return failedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve orphaned executions on startup");
            return 0;
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
