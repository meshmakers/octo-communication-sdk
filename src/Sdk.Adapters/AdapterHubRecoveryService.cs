using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Last line of defence for AB#5409: if an adapter that <i>was</i> registered at the
///     communication controller stays unregistered for longer than
///     <see cref="AdapterOptions.HubRegistrationRecoveryTimeout" />, this service stops the host so
///     the container restarts.
/// </summary>
/// <remarks>
///     <para>
///         Why a restart at all: on prod-1 a pod restart repaired the deaf adapters every single
///         time, with no configuration change — the process had simply lost the one thing it cannot
///         rebuild from the outside. A failing readiness probe cannot do this: Kubernetes never
///         restarts a container for readiness, only for liveness, and liveness must stay independent
///         of the hub (a controller outage must not take every adapter down with it). So the
///         self-heal is done here, deliberately and loudly, instead of by mis-wiring the liveness
///         probe.
///     </para>
///     <para>
///         Two guards keep this from turning into a restart loop:
///         <list type="number">
///             <item>
///                 It only arms after <see cref="IAdapterHubRegistrationState.HasEverRegistered" />.
///                 An adapter that never registered may be waiting for a tenant that is not enabled
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
/// </remarks>
public class AdapterHubRecoveryService(
    IAdapterHubClient adapterHubClient,
    IAdapterHubRegistrationState registrationState,
    IOptions<AdapterOptions> adapterOptions,
    AdapterLifetimeManagement lifetimeManagement) : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     How often the registration state is sampled.
    /// </summary>
    internal TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     UTC time the adapter was first seen unregistered in the current outage, or <c>null</c>
    ///     while it is registered. Internal for the tests.
    /// </summary>
    internal DateTime? UnregisteredSinceUtc { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = adapterOptions.Value;
        if (!options.HubRegistrationRecoveryEnabled)
        {
            Logger.Info("Adapter hub registration recovery is disabled by configuration");
            return;
        }

        Logger.Info(
            "Adapter hub registration recovery armed, restarting the adapter after {Timeout} without a hub registration",
            options.HubRegistrationRecoveryTimeout);

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

            bool restartRequired;
            try
            {
                restartRequired = Evaluate(DateTime.UtcNow);
            }
            catch (Exception e)
            {
                // This service stops the host by decision only. An exception escaping
                // ExecuteAsync stops the host as well, so a check that fails once would restart
                // the adapter on the spot instead of after the timeout (AB#5473: the hub client
                // threw while it was stopped for a tenant update). Sample again on the next tick.
                Logger.Error(e, "Adapter hub registration check failed, checking again in {Interval}",
                    CheckInterval);
                continue;
            }

            if (restartRequired)
            {
                // Error, not Warn: the adapter log carries WARN-level audit noise by the dozen per
                // pipeline run (AB#5409 item 3), and this line must still be findable afterwards.
                Logger.Error(
                    "Adapter has had no registration at the communication hub for {Timeout} (last registration {LastRegistered}, last failure: {LastFailure}). "
                    + "Stopping the adapter so the container restarts and re-registers",
                    options.HubRegistrationRecoveryTimeout,
                    registrationState.LastRegisteredUtc?.ToString("O") ?? "never",
                    registrationState.LastFailureMessage ?? "none");
                lifetimeManagement.Stop();
                return;
            }
        }
    }

    /// <summary>
    ///     Advances the outage tracking and reports whether the adapter must be restarted.
    ///     Internal so the decision can be tested without a timer.
    /// </summary>
    internal bool Evaluate(DateTime utcNow)
    {
        var options = adapterOptions.Value;

        // "Reachable by the controller" needs both: a live connection AND a registration on it.
        if (registrationState.IsRegistered && adapterHubClient.IsAlive)
        {
            UnregisteredSinceUtc = null;
            return false;
        }

        // Never registered in this process — not our case, and restarting would not help.
        if (!registrationState.HasEverRegistered)
        {
            UnregisteredSinceUtc = null;
            return false;
        }

        if (UnregisteredSinceUtc == null)
        {
            UnregisteredSinceUtc = utcNow;
            Logger.Warn(
                "Adapter lost its registration at the communication hub (connection alive: {IsAlive}). "
                + "Restarting the adapter if it does not recover within {Timeout}",
                adapterHubClient.IsAlive, options.HubRegistrationRecoveryTimeout);
            return false;
        }

        return utcNow - UnregisteredSinceUtc.Value >= options.HubRegistrationRecoveryTimeout;
    }
}
