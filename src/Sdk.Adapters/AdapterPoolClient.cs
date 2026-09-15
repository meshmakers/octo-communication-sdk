using System.Diagnostics;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     The pool member's half of the lease protocol (AB#4924, increment 6, concept §4).
/// </summary>
/// <remarks>
///     <para>
///         Receives <c>LeaseAsync</c> and <c>DrainAsync</c> from the controller and drives one lease
///         at a time through a fixed sequence:
///     </para>
///     <list type="number">
///         <item><description>enter the lease on <see cref="IAdapterLeaseScope" /> — from this moment the process has a tenant;</description></item>
///         <item><description>enter every <see cref="IAdapterLeaseParticipant" />, in registration order;</description></item>
///         <item><description>run the work item;</description></item>
///         <item><description>leave every participant that was entered, in <b>reverse</b> order;</description></item>
///         <item><description>leave the lease scope — from this moment the process has no tenant again;</description></item>
///         <item><description>report the release to the controller.</description></item>
///     </list>
///     <para>
///         🔴 <b>Steps 4 and 5 run on every path</b>, including an exception thrown out of step 2 or 3,
///         and step 6 runs even if 4 or 5 threw. That ordering is the isolation invariant: the tenant
///         must be gone from the process before the controller is told the member is free, because the
///         controller's next act is to hand it another tenant. Reporting first and cleaning up
///         afterwards would open exactly the window this design exists to close.
///     </para>
///     <para>
///         A second lease arriving while one is held is <b>refused</b>, not queued and not applied. The
///         controller's connection registry makes that impossible by construction, so reaching it means
///         the two views have diverged — and the safe answer to "I may already be serving somebody
///         else" is never "serve them both".
///     </para>
/// </remarks>
public sealed class AdapterPoolClient : IAdapterPoolHubCallbacks, IAsyncDisposable
{
    private readonly IAdapterLeaseScope _leaseScope;
    private readonly ILogger<AdapterPoolClient> _logger;
    private readonly AdapterPoolMemberOptions _options;
    private readonly IReadOnlyList<IAdapterLeaseParticipant> _participants;
    private readonly IAdapterPoolHubClient _poolHubClient;
    private readonly IAdapterLeaseWorkItem _workItem;

    // AB#4924 — what this member can run. Optional exactly as on the dedicated path
    // (AdapterExecutionService): a host that composed no data pipeline has no registry, and a member
    // without descriptors still registers and is still leasable.
    private readonly INodeSchemaRegistry? _nodeSchemaRegistry;
    private readonly IPipelineSchemaGenerator? _pipelineSchemaGenerator;

    // One lease at a time, and the gate is a semaphore rather than a lock because the whole path is
    // asynchronous. It also serialises a drain against an in-flight lease, which is what lets a drain
    // wait for the work item instead of yanking the tenant out from under it.
    private readonly SemaphoreSlim _leaseGate = new(1, 1);

    // Latches to 1 the first time the controller rejects a hub method with a HubException (a
    // controller build that pre-dates this contract). Subsequent calls are still attempted - they
    // will keep throwing - but the warning fires once, so a heartbeat every 30 s does not flood the
    // log. Same once-only degrade pattern as AB#4917's scale-status channel.
    private int _controllerUnsupportedLogged;

    private volatile bool _isDraining;

    /// <summary>
    ///     Constructor.
    /// </summary>
    /// <param name="leaseScope">The process's lease-aware tenant scope.</param>
    /// <param name="poolHubClient">The management connection to the controller.</param>
    /// <param name="participants">Everything that has to be set up and torn down per lease.</param>
    /// <param name="workItem">What to run while the lease is held.</param>
    /// <param name="options">The pool this member belongs to.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="nodeSchemaRegistry">The nodes this member can execute; null when the host composed no data pipeline.</param>
    /// <param name="pipelineSchemaGenerator">The composite pipeline schema generator; null when the host composed no data pipeline.</param>
    public AdapterPoolClient(IAdapterLeaseScope leaseScope, IAdapterPoolHubClient poolHubClient,
        IEnumerable<IAdapterLeaseParticipant> participants, IAdapterLeaseWorkItem workItem,
        IOptions<AdapterPoolMemberOptions> options, ILogger<AdapterPoolClient> logger,
        INodeSchemaRegistry? nodeSchemaRegistry = null,
        IPipelineSchemaGenerator? pipelineSchemaGenerator = null)
    {
        _leaseScope = leaseScope;
        _poolHubClient = poolHubClient;
        _participants = participants.ToList();
        _workItem = workItem;
        _options = options.Value;
        _logger = logger;
        _nodeSchemaRegistry = nodeSchemaRegistry;
        _pipelineSchemaGenerator = pipelineSchemaGenerator;
    }

    /// <summary>Whether this member was asked to drain and takes no further lease.</summary>
    public bool IsDraining => _isDraining;

    /// <summary>
    ///     Registers this process as a member of its configured pool.
    /// </summary>
    /// <returns>The controller's answer, or <c>null</c> when the controller does not support the hub.</returns>
    public async Task<PoolMemberRegistrationResultDto?> RegisterAsync()
    {
        // 🔴 AB#4924 — the SAME projection the dedicated path sends on RegisterAdapterWithSchemaAsync.
        // Without it the controller has no node descriptors for a pool member at all, and a BORROWER's
        // DeployPipeline falls back to the name list: the execution class of every leased pipeline
        // stays at the CK default Batch, process-bound triggers go unclassified, and the definition is
        // validated against no schema.
        var nodeDescriptors = AdapterNodeDescriptorProjection.TryProject(_nodeSchemaRegistry,
            e => _logger.LogWarning(e, "Failed to project node descriptors; registering this pool member without them"));
        var pipelineSchemaJson = AdapterNodeDescriptorProjection.TryGenerateSchema(_pipelineSchemaGenerator,
            e => _logger.LogWarning(e, "Failed to generate the pipeline schema; registering this pool member without it"));

        var registration = new PoolMemberRegistrationDto
        {
            PoolTenantId = _options.PoolTenantId ?? string.Empty,
            PoolRtId = _options.PoolRtId ?? string.Empty,
            MemberId = _options.EffectiveMemberId,
            NodeDescriptors = nodeDescriptors ?? [],
            PipelineSchemaJson = pipelineSchemaJson
        };

        try
        {
            var result = await _poolHubClient.RegisterPoolMemberAsync(registration);
            if (result.Accepted)
            {
                _logger.LogInformation(
                    "Registered as member '{MemberId}' of adapter pool {PoolRtId} in tenant '{PoolTenantId}' with " +
                    "{NodeCount} node descriptor(s) and {SchemaState} pipeline schema",
                    result.MemberId, registration.PoolRtId, registration.PoolTenantId,
                    registration.NodeDescriptors.Count, pipelineSchemaJson == null ? "no" : "a");
            }
            else
            {
                _logger.LogWarning(
                    "The controller refused this pool-member registration for pool {PoolRtId} in tenant " +
                    "'{PoolTenantId}': {StatusMessage}",
                    registration.PoolRtId, registration.PoolTenantId, result.StatusMessage);
            }

            return result;
        }
        catch (HubException e)
        {
            WarnOnceAboutUnsupportedController(e, nameof(IAdapterPoolHub.RegisterPoolMemberAsync));
            return null;
        }
    }

    /// <inheritdoc />
    public async Task LeaseAsync(LeaseDto lease)
    {
        if (_isDraining)
        {
            // A lease that raced the drain. Refusing it is safe and honest: the controller re-queues.
            await ReportReleaseAsync(lease, LeaseReleaseReasonDto.Drained, success: false,
                "The member is draining and did not take the lease.");
            return;
        }

        if (!await _leaseGate.WaitAsync(TimeSpan.Zero))
        {
            // 🔴 Not queued, not applied. The controller's registry makes a second concurrent lease
            // impossible, so arriving here means the two views have diverged — and the safe answer to
            // "I may already be serving somebody else" is never "serve them both".
            _logger.LogError(
                "Refused lease '{LeaseId}' for tenant '{TenantId}': this member already holds a lease. " +
                "The controller and the member disagree about what this member is doing.",
                lease.LeaseId, lease.TenantId);
            await ReportReleaseAsync(lease, LeaseReleaseReasonDto.Failed, success: false,
                "The member already holds a lease and refused this one.");
            return;
        }

        try
        {
            await RunLeaseAsync(lease);
        }
        finally
        {
            _leaseGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DrainAsync(string reason)
    {
        _logger.LogInformation("This pool member was asked to drain: {Reason}", reason);
        _isDraining = true;

        // Waits for an in-flight lease to finish rather than interrupting it. A drain that yanked the
        // tenant out from under a running work item would produce exactly the half-released state
        // concept §6 says makes a member unfit for re-use.
        await _leaseGate.WaitAsync();
        _leaseGate.Release();
    }

    /// <summary>
    ///     The whole lease, from entering the tenant to reporting the release.
    /// </summary>
    private async Task RunLeaseAsync(LeaseDto lease)
    {
        // 🔴 Every field named individually; the lease object is never interpolated as a whole. It
        // overrides ToString for the same reason, but a log statement that reached for the secret
        // explicitly would defeat that.
        _logger.LogInformation(
            "Taking lease '{LeaseId}' for tenant '{TenantId}' from pool {PoolRtId} of tenant " +
            "'{PoolTenantId}', expires {ExpiresAtUtc:O}",
            lease.LeaseId, lease.TenantId, lease.PoolRtId, lease.PoolTenantId, lease.ExpiresAtUtc);

        var outcome = LeaseWorkOutcome.Failed("The lease was not entered.");
        var entered = new List<IAdapterLeaseParticipant>(_participants.Count);
        IDisposable? leaseScope = null;

        // 🔴 AB#4924 increment 9. The member is the ONLY party that can measure this. The controller
        // stamps StartedAt on a leased execution at claim time, so its own view of "how long did the
        // pipeline run" is identical to "how long was the member held" by construction, and the
        // per-lease warm-up concept §2.3 exists to expose would read as zero forever. Timed around
        // the work item alone: everything outside it — entering the lease scope, the borrower login,
        // the CK model, the pipeline registration, and every leave — is the overhead, and the
        // controller derives it by subtraction.
        long? workDurationMs = null;

        try
        {
            leaseScope = _leaseScope.BeginLease(lease.TenantId);

            foreach (var participant in _participants)
            {
                await participant.EnterLeaseAsync(lease, CancellationToken.None);
                // Recorded AFTER a successful enter: a participant whose enter threw has nothing to
                // leave, and calling its leave anyway is how a half-constructed state gets torn down
                // in a way nobody designed.
                entered.Add(participant);
            }

            var workStarted = Stopwatch.GetTimestamp();
            try
            {
                outcome = await _workItem.RunAsync(lease, CancellationToken.None);
            }
            finally
            {
                // Stamped even when the work item threw: a failed run still held the tenant for that
                // long, and dropping the sample would quietly bias the distribution towards the runs
                // that succeeded.
                workDurationMs = (long)Stopwatch.GetElapsedTime(workStarted).TotalMilliseconds;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Lease '{LeaseId}' for tenant '{TenantId}' failed",
                lease.LeaseId, lease.TenantId);
            outcome = LeaseWorkOutcome.Failed(e.Message);
        }
        finally
        {
            // 🔴 Reverse order, and every leave is attempted even if an earlier one threw. A leave
            // skipped because its neighbour failed is precisely how a tenant survives a release.
            for (var i = entered.Count - 1; i >= 0; i--)
            {
                try
                {
                    await entered[i].LeaveLeaseAsync(lease, CancellationToken.None);
                }
                catch (Exception e)
                {
                    _logger.LogError(e,
                        "A lease participant of type {ParticipantType} failed to leave lease '{LeaseId}' for " +
                        "tenant '{TenantId}'. This member may still hold tenant-scoped state and must not " +
                        "take another lease.",
                        entered[i].GetType().Name, lease.LeaseId, lease.TenantId);
                    // Concept §6: post-lease cleanliness is unproven, so the member drains rather
                    // than being re-used. Not a best-effort shrug — the whole invariant is that a
                    // member that cannot prove it is clean never serves a second tenant.
                    _isDraining = true;
                    outcome = LeaseWorkOutcome.Failed(
                        $"A lease participant failed to release tenant state: {e.Message}");
                }
            }

            leaseScope?.Dispose();
        }

        var reason = _isDraining
            ? LeaseReleaseReasonDto.Drained
            : outcome.Success
                ? LeaseReleaseReasonDto.Completed
                : LeaseReleaseReasonDto.Failed;

        // Only now — the tenant is gone from the process, so the controller is free to hand this
        // member another one.
        await ReportReleaseAsync(lease, reason, outcome.Success, outcome.StatusMessage, outcome.OutputData,
            workDurationMs);
    }

    private async Task ReportReleaseAsync(LeaseDto lease, LeaseReleaseReasonDto reason, bool success,
        string? statusMessage, string? outputData = null, long? workDurationMs = null)
    {
        try
        {
            await _poolHubClient.ReleaseLeaseAsync(new LeaseResultDto
            {
                LeaseId = lease.LeaseId,
                Reason = reason,
                Success = success,
                StatusMessage = statusMessage,
                // AB#4924 §9.9 / D4: the only route a leased execution's output has back to its
                // entity. The member has no adapter-hub connection to report an execution end on.
                OutputData = outputData,
                // Null on every path where the work item never ran — a lease refused because the
                // member is draining or already holds one. The controller records no overhead
                // sample for those rather than a fabricated zero (AB#4924 increment 9).
                WorkDurationMs = workDurationMs,
                ReleasedAtUtc = DateTime.UtcNow
            });
        }
        catch (HubException e)
        {
            WarnOnceAboutUnsupportedController(e, nameof(IAdapterPoolHub.ReleaseLeaseAsync));
        }
        catch (Exception e)
        {
            // The lease is already released locally; the controller will expire it on TTL. Losing the
            // report must never leave the member holding the tenant.
            _logger.LogWarning(e,
                "Could not report the release of lease '{LeaseId}'; the controller will expire it on TTL",
                lease.LeaseId);
        }
    }

    /// <summary>
    ///     Sends one heartbeat, naming the lease this member believes it holds.
    /// </summary>
    public async Task HeartbeatAsync()
    {
        try
        {
            await _poolHubClient.HeartbeatAsync(new PoolMemberHeartbeatDto
            {
                MemberId = _options.EffectiveMemberId,
                ActiveLeaseId = _leaseScope.LeaseTenantId is null ? null : _leaseScope.LeaseTenantId,
                SampledAtUtc = DateTime.UtcNow
            });
        }
        catch (HubException e)
        {
            WarnOnceAboutUnsupportedController(e, nameof(IAdapterPoolHub.HeartbeatAsync));
        }
    }

    /// <summary>
    ///     Skew rule, as for AB#4917: controller, <c>octo-sdk</c> and the adapter SDK ship together,
    ///     so this only covers a rolling-upgrade window. One warning, then silence — a heartbeat every
    ///     30 s would otherwise turn a skew into a log flood.
    /// </summary>
    private void WarnOnceAboutUnsupportedController(HubException e, string methodName)
    {
        if (Interlocked.CompareExchange(ref _controllerUnsupportedLogged, 1, 0) == 0)
        {
            _logger.LogWarning(e,
                "The communication controller does not accept {MethodName} — it pre-dates the adapter pool " +
                "lease contract (AB#4924). This member cannot be leased until the controller is upgraded.",
                methodName);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _leaseGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
