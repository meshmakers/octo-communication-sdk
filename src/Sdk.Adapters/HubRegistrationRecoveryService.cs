using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     The restart decision shared by both kinds of adapter process: a process that <i>was</i>
///     registered at the communication controller and stays unregistered for longer than
///     <see cref="AdapterOptions.HubRegistrationRecoveryTimeout" /> stops its host so the container
///     restarts.
/// </summary>
/// <remarks>
///     <para>
///         A dedicated adapter registers at the tenant's adapter hub
///         (<see cref="AdapterHubRecoveryService" />, AB#5409); an adapter pool member registers at the
///         tenant-free adapter pool hub (<see cref="AdapterPoolHubRecoveryService" />, AB#4924 AP-I5).
///         The two differ only in which connection they sample — the decision, its two guards and
///         its configuration are one policy, so they live here once.
///     </para>
///     <para>
///         Two guards keep this from turning into a restart loop:
///         <list type="number">
///             <item>
///                 It only arms after <see cref="IAdapterHubRegistrationState.HasEverRegistered" />.
///                 A process that never registered may be waiting for a tenant that is not enabled
///                 yet, or for a controller that was never deployed — restarting that in a loop
///                 repairs nothing and hides the real cause.
///             </item>
///             <item>
///                 The timeout is generous (15 minutes by default, far beyond any reconnect the
///                 SignalR client performs by itself) and can be disabled outright with
///                 <see cref="AdapterOptions.HubRegistrationRecoveryEnabled" />.
///             </item>
///         </list>
///     </para>
///     <para>
///         🔴 It stops the host by decision only, never by a failing check (AB#5473): an exception
///         escaping <see cref="ExecuteAsync" /> would stop the host as well, on the spot instead of
///         after the timeout. A connection state that cannot be read therefore counts as "not alive".
///     </para>
/// </remarks>
public abstract class HubRegistrationRecoveryService : BackgroundService
{
    private readonly IOptions<AdapterOptions> _adapterOptions;
    private readonly string _hubName;
    private readonly AdapterLifetimeManagement _lifetimeManagement;
    private readonly Logger _logger;
    private readonly IAdapterHubRegistrationState _registrationState;

    /// <summary>
    ///     Constructor.
    /// </summary>
    /// <param name="registrationState">The registration state the process writes on every (re)registration.</param>
    /// <param name="adapterOptions">Recovery switch and timeout.</param>
    /// <param name="lifetimeManagement">Stops the host.</param>
    /// <param name="hubName">The hub's name as it appears in the log, e.g. "adapter hub".</param>
    protected HubRegistrationRecoveryService(IAdapterHubRegistrationState registrationState,
        IOptions<AdapterOptions> adapterOptions, AdapterLifetimeManagement lifetimeManagement, string hubName)
    {
        _registrationState = registrationState;
        _adapterOptions = adapterOptions;
        _lifetimeManagement = lifetimeManagement;
        _hubName = hubName;
        _logger = LogManager.GetLogger(GetType().FullName ?? nameof(HubRegistrationRecoveryService));
    }

    /// <summary>
    ///     How often the registration state is sampled.
    /// </summary>
    internal TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     UTC time the process was first seen unregistered in the current outage, or <c>null</c>
    ///     while it is registered. Internal for the tests.
    /// </summary>
    internal DateTime? UnregisteredSinceUtc { get; private set; }

    /// <summary>
    ///     Whether the connection to the hub is currently up. May throw; the caller treats a throw as
    ///     "not alive".
    /// </summary>
    protected abstract bool IsConnectionAlive { get; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _adapterOptions.Value;
        if (!options.HubRegistrationRecoveryEnabled)
        {
            _logger.Info("Registration recovery for the {HubName} is disabled by configuration", _hubName);
            return;
        }

        _logger.Info(
            "Registration recovery for the {HubName} armed, restarting the process after {Timeout} without a registration",
            _hubName, options.HubRegistrationRecoveryTimeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (Evaluate(DateTime.UtcNow))
            {
                // Error, not Warn: the adapter log carries WARN-level audit noise by the dozen per
                // pipeline run (AB#5409 item 3), and this line must still be findable afterwards.
                _logger.Error(
                    "No registration at the {HubName} of the communication controller for {Timeout} (last registration {LastRegistered}, last failure: {LastFailure}). "
                    + "Stopping the process so the container restarts and re-registers",
                    _hubName, options.HubRegistrationRecoveryTimeout,
                    _registrationState.LastRegisteredUtc?.ToString("O") ?? "never",
                    _registrationState.LastFailureMessage ?? "none");
                _lifetimeManagement.Stop();
                return;
            }
        }
    }

    /// <summary>
    ///     Advances the outage tracking and reports whether the process must be restarted.
    ///     Internal so the decision can be tested without a timer.
    /// </summary>
    internal bool Evaluate(DateTime utcNow)
    {
        var options = _adapterOptions.Value;
        var isAlive = ReadIsConnectionAlive();

        // "Reachable by the controller" needs both: a live connection AND a registration on it.
        if (_registrationState.IsRegistered && isAlive)
        {
            UnregisteredSinceUtc = null;
            return false;
        }

        // Never registered in this process — not our case, and restarting would not help.
        if (!_registrationState.HasEverRegistered)
        {
            UnregisteredSinceUtc = null;
            return false;
        }

        if (UnregisteredSinceUtc == null)
        {
            UnregisteredSinceUtc = utcNow;
            _logger.Warn(
                "Lost the registration at the {HubName} of the communication controller (connection alive: {IsAlive}). "
                + "Restarting the process if it does not recover within {Timeout}",
                _hubName, isAlive, options.HubRegistrationRecoveryTimeout);
            return false;
        }

        return utcNow - UnregisteredSinceUtc.Value >= options.HubRegistrationRecoveryTimeout;
    }

    /// <summary>
    ///     Reads the connection state without ever throwing: a state that cannot be read counts as
    ///     "not alive" (AB#5473). Counting the sample as an outage keeps the watchdog armed —
    ///     skipping it would let a read that fails for good hide an unreachable process forever.
    /// </summary>
    private bool ReadIsConnectionAlive()
    {
        try
        {
            return IsConnectionAlive;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Reading the {HubName} connection state failed, treating the connection as not alive",
                _hubName);
            return false;
        }
    }
}
