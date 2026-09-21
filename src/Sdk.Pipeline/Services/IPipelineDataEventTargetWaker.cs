using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     Makes sure the workload executing a target pipeline is running before a pipeline data event
///     is published to it (AB#5231). The event queue is durable, so a message for a hibernated
///     OnDemand target waits on the broker — but nothing else would bring that target up to consume
///     it, and on a dedicated adapter the send never passes the controller, which owns every other
///     wake gate. This is the send-path gate.
/// </summary>
public interface IPipelineDataEventTargetWaker
{
    /// <summary>
    ///     Returns once the target's workload is running, or at once when it is this process, an
    ///     AlwaysOn workload, or a tenant without scale-to-zero. Never throws: a failed wake is
    ///     logged and the publish goes ahead — the durable queue keeps the message for the next wake.
    /// </summary>
    Task EnsureTargetRunningAsync(string tenantId, RtEntityId targetPipelineRtEntityId,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     The default when the process has no controller connection to ask (tests, tools, a pool member
///     that reaches the controller through the pool hub only): nothing to wake, publish right away.
/// </summary>
public sealed class NoPipelineDataEventTargetWaker : IPipelineDataEventTargetWaker
{
    /// <inheritdoc />
    public Task EnsureTargetRunningAsync(string tenantId, RtEntityId targetPipelineRtEntityId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
///     The adapter's waker: skips a target registered in THIS process (the sender is running, so the
///     target is too — the common in-workload chaining case pays nothing), and otherwise asks the
///     controller over the adapter hub, which resolves the target's workload and wakes it.
/// </summary>
public sealed class HubPipelineDataEventTargetWaker(
    IAdapterHubClient adapterHubClient,
    IPipelineRegistryService pipelineRegistryService,
    ILogger<HubPipelineDataEventTargetWaker> logger) : IPipelineDataEventTargetWaker
{
    /// <inheritdoc />
    public async Task EnsureTargetRunningAsync(string tenantId, RtEntityId targetPipelineRtEntityId,
        CancellationToken cancellationToken = default)
    {
        if (pipelineRegistryService.TryGetPipelineRegistration(tenantId, targetPipelineRtEntityId, out _))
        {
            return;
        }

        try
        {
            await adapterHubClient.EnsurePipelineWorkloadRunningAsync(targetPipelineRtEntityId);
        }
        catch (Exception e)
        {
            // Not fatal for the publish: the queue is durable, and the next demand signal for the
            // target (a cron co-wake, an HTTP request, a later event) drains it. Loud, because a
            // target that is never woken otherwise is exactly the silence AB#5231 was about.
            logger.LogWarning(e,
                "[{TenantId}] Could not make sure the workload of pipeline '{TargetPipelineRtId}' is running before " +
                "publishing a pipeline data event to it; the event is queued durably and waits for the next wake",
                tenantId, targetPipelineRtEntityId.RtId);
        }
    }
}
