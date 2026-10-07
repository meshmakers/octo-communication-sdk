using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Runs this process as a member of an adapter pool (AB#4924): it opens the tenant-free
///     management connection, registers on every connect, and heartbeats at the cadence the
///     controller hands back.
/// </summary>
/// <remarks>
///     <para>
///         This is the piece that turns <see cref="AdapterPoolClient" /> from a class into a running
///         member. Increments 6 and 7 built the client, the hub, the scheduler and the work item, but
///         nothing composed them into a process: <c>AddAdapterPoolMember()</c> registered the client
///         and no hosted service ever started it, so a correctly configured member connected to
///         nothing and was never leased.
///     </para>
///     <para>
///         🔴 <b>Registration happens inside the connect callback, not once after
///         <c>StartAsync</c>.</b> The controller binds a member to its SignalR <i>connection</i>
///         (<c>AdapterPoolConnectionManager</c>), so a reconnect leaves the old mapping behind. A
///         member that registered only once would sit there connected and healthy-looking, and never
///         receive another lease — the failure mode is silence, which is why this is a callback and
///         not a line of startup code.
///     </para>
///     <para>
///         <b>The heartbeat cadence is not a local option.</b> The controller returns it at
///         registration (<c>PoolMemberRegistrationResultDto.HeartbeatIntervalSeconds</c>), so the two
///         sides cannot drift apart and an operator has exactly one place to change it.
///         <see cref="FallbackHeartbeatInterval" /> covers only the window before the first answer
///         arrives, and the case where an older controller answers with nothing.
///     </para>
///     <para>
///         <b>Member recovery (AB#4924 AP-I5), the pool member's counterpart of AB#5409/AB#5415.</b>
///         Registration — not the connection — is the signal, recorded in
///         <see cref="AdapterPoolClient.RegistrationState" /> and read by
///         <see cref="AdapterPoolHubReadinessHealthCheck" /> and <see cref="AdapterPoolHubRecoveryService" />.
///         This service adds the parts that only the member itself can do:
///     </para>
///     <list type="bullet">
///         <item><description>
///             <b>Re-register instead of heartbeating into nothing.</b> A registration the controller
///             refused (a controller pod that is shutting down, an enforcing tenant binding) or a
///             heartbeat it rejected leaves the connection up and the member unleasable. The heartbeat
///             tick then registers again rather than sending a heartbeat nobody records.
///         </description></item>
///         <item><description>
///             <b>Never offer a busy process as free.</b> A member that reconnects while its lease is
///             still running defers the registration until the lease is released: the controller has
///             already interrupted and re-queued that lease (concept §6), and a fresh registration
///             would make it hand the member the next lease at once — which the member then has to
///             refuse, failing that borrower's execution for nothing.
///         </description></item>
///         <item><description>
///             <b>Flush the CK model cache of the recently leased tenants after a reconnect</b>
///             (AB#5415). <c>CkModelChanged</c> never reaches a member at all — it has no adapter-hub
///             connection — so per-lease load/unload (the host's lease participants) is what keeps a
///             member current between leases. What a reconnect can leave stale is the tenant of the
///             lease running across it and, for a host without such a participant, the last tenant
///             entered. Flushed before the registration, so no new lease can warm a cache that is
///             about to be dropped.
///         </description></item>
///     </list>
/// </remarks>
public sealed class AdapterPoolMemberService : BackgroundService
{
    /// <summary>
    ///     Used until the controller's first registration answer names a cadence, and whenever it
    ///     answers with a non-positive one. Deliberately equal to the controller's own default so the
    ///     fallback is not a second, quietly different policy.
    /// </summary>
    internal static readonly TimeSpan FallbackHeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly IAdapterService? _adapterService;
    private readonly IAdapterPoolHubClient _hubClient;
    private readonly ILogger<AdapterPoolMemberService> _logger;
    private readonly IOptions<AdapterPoolMemberOptions> _options;
    private readonly AdapterPoolClient _poolClient;

    private TimeSpan _heartbeatInterval = FallbackHeartbeatInterval;

    /// <summary>Creates the pool-member service.</summary>
    /// <param name="poolClient">The member's half of the lease protocol.</param>
    /// <param name="hubClient">The management connection.</param>
    /// <param name="options">The pool this member belongs to.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="adapterService">
    ///     The host's adapter service, whose <see cref="IAdapterService.CkModelChangedAsync" /> drops a
    ///     tenant's CK model cache. Optional: a host without one has no cache to flush.
    /// </param>
    public AdapterPoolMemberService(AdapterPoolClient poolClient, IAdapterPoolHubClient hubClient,
        IOptions<AdapterPoolMemberOptions> options, ILogger<AdapterPoolMemberService> logger,
        IAdapterService? adapterService = null)
    {
        _poolClient = poolClient;
        _hubClient = hubClient;
        _options = options;
        _logger = logger;
        _adapterService = adapterService;
    }

    /// <summary>The cadence currently in force; the controller's answer once one has arrived.</summary>
    internal TimeSpan HeartbeatInterval => _heartbeatInterval;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.IsEnabled)
        {
            // Defensive: the builder only registers this service when the section is set, and
            // AdapterPoolMemberConfigurationGuard has already refused a section that is set and does
            // not bind (AB#5303 item 5). Reaching this line means the host composed a member without
            // configuration at all, so returning quietly rather than throwing is still right.
            _logger.LogWarning(
                "Adapter pool member service started without a configured pool; set " +
                "OCTO_ADAPTERPOOL__ADAPTERPOOLTENANTID and OCTO_ADAPTERPOOL__ADAPTERPOOLRTID. Doing nothing.");
            return;
        }

        _logger.LogInformation(
            "Starting as member '{MemberId}' of adapter pool {AdapterPoolRtId} in lending tenant '{AdapterPoolTenantId}'",
            _options.Value.EffectiveMemberId, _options.Value.AdapterPoolRtId, _options.Value.AdapterPoolTenantId);

        try
        {
            await _hubClient.StartAsync(OnConnectedAsync, stoppingToken);
            _hubClient.EnableReconnect(OnConnectedAsync);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // 🔴 AB#5080: an unhandled exception out of a BackgroundService stops the whole host by
            // default. A controller that is not up yet must not kill the member process.
            _logger.LogError(e, "Could not open the adapter pool management connection");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await TickAsync();
        }
    }

    /// <summary>
    ///     One heartbeat tick: a heartbeat while registered, a registration attempt while the
    ///     connection is up but the controller holds no registration, nothing while the connection is
    ///     down (the SignalR client owns the reconnect, and its connect callback registers).
    ///     Internal so the decision can be tested without the timer.
    /// </summary>
    internal async Task TickAsync()
    {
        try
        {
            if (!_hubClient.IsAlive)
            {
                return;
            }

            if (_poolClient.RegistrationState.IsRegistered)
            {
                await _poolClient.HeartbeatAsync();
                return;
            }

            if (_poolClient.CurrentLease is { } lease)
            {
                // See the class remarks: never offer a busy process as free.
                _logger.LogDebug(
                    "Not registering yet: lease '{LeaseId}' is still running on this member", lease.LeaseId);
                return;
            }

            _logger.LogInformation(
                "The controller holds no registration for this pool member ({Reason}); registering again",
                _poolClient.RegistrationState.LastFailureMessage ?? "never registered");
            await RegisterAsync();
        }
        catch (Exception e)
        {
            // Same reasoning as above: a missed heartbeat is a reconnect, not a reason to exit.
            _logger.LogWarning(e, "Adapter pool heartbeat failed; will retry on the next interval");
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            await _hubClient.StopAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to close the adapter pool management connection cleanly");
        }
    }

    /// <summary>
    ///     Runs on every connect and every reconnect. See the remark on the class for why registering
    ///     once would be wrong. Internal for the tests.
    /// </summary>
    internal async Task OnConnectedAsync(bool reconnected)
    {
        if (reconnected)
        {
            // A new connection carries no registration, whatever the state says about the old one.
            // Cleared first, so neither the heartbeat tick nor the readiness probe acts on a
            // registration that died with the previous connection.
            _poolClient.RegistrationState.MarkNotRegistered("re-registering after a reconnect");

            await FlushCkModelCacheAfterReconnectAsync();

            if (_poolClient.CurrentLease is { } lease)
            {
                // See the class remarks. The heartbeat tick registers once the lease is released.
                _poolClient.RegistrationState.MarkNotRegistered(
                    $"registration deferred until lease '{lease.LeaseId}' is released");
                _logger.LogWarning(
                    "Reconnected while lease '{LeaseId}' for tenant '{TenantId}' is still running. The controller has " +
                    "interrupted and re-queued it; this member registers again once the lease is released",
                    lease.LeaseId, lease.TenantId);
                return;
            }
        }

        await RegisterAsync();

        if (reconnected && _poolClient.RegistrationState.IsRegistered)
        {
            _logger.LogInformation("Re-registered as pool member after a reconnect");
        }
    }

    private async Task RegisterAsync()
    {
        var result = await _poolClient.RegisterAsync();

        if (result is { HeartbeatIntervalSeconds: > 0 })
        {
            _heartbeatInterval = TimeSpan.FromSeconds(result.HeartbeatIntervalSeconds);
        }
    }

    /// <summary>
    ///     Drops the CK model cache of the tenants this member leased most recently (AB#5415 for pool
    ///     members). Best-effort: the cache reloads lazily on the next lease, so a failure here must
    ///     not fail the reconnect — it only leaves the member where it already was.
    /// </summary>
    private async Task FlushCkModelCacheAfterReconnectAsync()
    {
        if (_adapterService is null)
        {
            return;
        }

        foreach (var tenantId in _poolClient.RecentlyLeasedTenantIds)
        {
            try
            {
                _logger.LogInformation(
                    "Invalidating the CK model cache of recently leased tenant '{TenantId}' after a reconnect; CK model " +
                    "changes announced while disconnected were not delivered",
                    tenantId);
                await _adapterService.CkModelChangedAsync(tenantId);
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Invalidating the CK model cache after a reconnect failed for recently leased tenant '{TenantId}'",
                    tenantId);
            }
        }
    }
}
