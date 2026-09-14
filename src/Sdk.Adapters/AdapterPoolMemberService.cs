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
/// </remarks>
public sealed class AdapterPoolMemberService : BackgroundService
{
    /// <summary>
    ///     Used until the controller's first registration answer names a cadence, and whenever it
    ///     answers with a non-positive one. Deliberately equal to the controller's own default so the
    ///     fallback is not a second, quietly different policy.
    /// </summary>
    internal static readonly TimeSpan FallbackHeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly IAdapterPoolHubClient _hubClient;
    private readonly ILogger<AdapterPoolMemberService> _logger;
    private readonly IOptions<AdapterPoolMemberOptions> _options;
    private readonly AdapterPoolClient _poolClient;

    private TimeSpan _heartbeatInterval = FallbackHeartbeatInterval;

    /// <summary>Creates the pool-member service.</summary>
    public AdapterPoolMemberService(AdapterPoolClient poolClient, IAdapterPoolHubClient hubClient,
        IOptions<AdapterPoolMemberOptions> options, ILogger<AdapterPoolMemberService> logger)
    {
        _poolClient = poolClient;
        _hubClient = hubClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>The cadence currently in force; the controller's answer once one has arrived.</summary>
    internal TimeSpan HeartbeatInterval => _heartbeatInterval;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.IsEnabled)
        {
            // Defensive: the builder only registers this service when the section is set. Returning
            // quietly rather than throwing keeps a misconfiguration from taking the host down.
            _logger.LogWarning(
                "Adapter pool member service started without a configured pool; set " +
                "OCTO_ADAPTERPOOL__POOLTENANTID and OCTO_ADAPTERPOOL__POOLRTID. Doing nothing.");
            return;
        }

        _logger.LogInformation(
            "Starting as member '{MemberId}' of adapter pool {PoolRtId} in lending tenant '{PoolTenantId}'",
            _options.Value.EffectiveMemberId, _options.Value.PoolRtId, _options.Value.PoolTenantId);

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

            try
            {
                await _poolClient.HeartbeatAsync();
            }
            catch (Exception e)
            {
                // Same reasoning as above: a missed heartbeat is a reconnect, not a reason to exit.
                _logger.LogWarning(e, "Adapter pool heartbeat failed; will retry on the next interval");
            }
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
    ///     once would be wrong.
    /// </summary>
    private async Task OnConnectedAsync(bool reconnected)
    {
        var result = await _poolClient.RegisterAsync();

        if (result is { HeartbeatIntervalSeconds: > 0 })
        {
            _heartbeatInterval = TimeSpan.FromSeconds(result.HeartbeatIntervalSeconds);
        }

        if (reconnected)
        {
            _logger.LogInformation("Re-registered as pool member after a reconnect");
        }
    }
}
