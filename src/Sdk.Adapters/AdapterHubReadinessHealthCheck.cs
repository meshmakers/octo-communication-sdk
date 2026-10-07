using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Readiness check: an adapter the communication controller cannot reach is not ready.
/// </summary>
/// <remarks>
///     <para>
///         Registered with the <c>ready</c> tag, so it governs <c>/healthz/ready</c>. Before AB#5409
///         <c>/healthz/ready</c> only reported the generic 15-second startup task, so a deaf adapter
///         answered 200 forever: five prod-1 adapters lost their hub registration during a node
///         outage and nothing — not the probe, not the adapter entity, not the pod status — said so.
///     </para>
///     <para>
///         Readiness, not liveness: liveness must stay about the process (see
///         <c>/healthz/live</c>, which deliberately evaluates no check at all). Note that a failing
///         readiness probe does <b>not</b> restart a pod — it only takes it out of the Service
///         endpoints and shows it as <c>0/1</c>. The automatic recovery is
///         <see cref="AdapterHubRecoveryService" />.
///     </para>
///     <para>
///         The decision and its two softeners are shared with the pool member's counterpart,
///         <see cref="AdapterPoolHubReadinessHealthCheck" />; see
///         <see cref="HubRegistrationReadinessHealthCheck" />.
///     </para>
/// </remarks>
public sealed class AdapterHubReadinessHealthCheck(
    IAdapterHubClient adapterHubClient,
    IAdapterHubRegistrationState registrationState,
    IOptions<AdapterOptions> adapterOptions)
    : HubRegistrationReadinessHealthCheck(registrationState, adapterOptions, "Adapter", "communication hub")
{
    /// <inheritdoc />
    protected override bool IsConnectionAlive => adapterHubClient.IsAlive;
}

/// <summary>
///     Readiness check of an adapter pool member (AB#4924 AP-I5): a member the communication
///     controller has no registration for is never leased, so it is not ready.
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Readiness follows the registration at the pool hub, never a lease.</b> An idle member
///         holds no tenant and is exactly as ready as a busy one — a check that waited for a lease
///         would keep every member of an idle pool <c>0/1</c> for ever. The same reasoning is why
///         the dedicated adapter's <see cref="AdapterHubReadinessHealthCheck" /> must never be
///         registered on a member: a member has no adapter hub connection, so it would never pass.
///     </para>
///     <para>
///         A member receives no traffic through its Service — work arrives on the management
///         connection — so a failing probe here changes no routing. What it buys is visibility
///         (<c>0/1</c> instead of a green pod that is never leased) and a rollout that does not report
///         success for members that cannot register.
///     </para>
///     <para>
///         🔴 <b>A draining member is not ready</b> (AB#5864), whatever its registration says: it takes
///         no further lease, so a green probe would show a pod that is never leased again as healthy
///         — exactly the test-2-dev picture of a pool that was dead behind a <c>1/1</c> pod.
///         <see cref="AdapterPoolMemberDrainExitService" /> stops the process once it is idle.
///     </para>
/// </remarks>
public sealed class AdapterPoolHubReadinessHealthCheck(
    IAdapterPoolHubClient adapterPoolHubClient,
    IAdapterHubRegistrationState registrationState,
    IOptions<AdapterOptions> adapterOptions,
    AdapterPoolClient? poolClient = null)
    : HubRegistrationReadinessHealthCheck(registrationState, adapterOptions, "Adapter pool member",
        "adapter pool hub")
{
    /// <inheritdoc />
    protected override bool IsConnectionAlive => adapterPoolHubClient.IsAlive;

    /// <inheritdoc />
    protected override string? NotReadyReason => poolClient is { IsDraining: true }
        ? $"Adapter pool member is draining and takes no further lease ({poolClient.DrainReason ?? "no reason recorded"}); " +
          "it exits once idle and the pool restarts it."
        : null;
}

/// <summary>
///     The readiness decision shared by dedicated adapters and adapter pool members: ready while
///     registered on a live connection, and during a startup grace period before the first
///     registration.
/// </summary>
/// <remarks>
///     Two deliberate softeners keep this from becoming a hair trigger:
///     <list type="bullet">
///         <item>
///             <see cref="AdapterOptions.HubReadinessGracePeriod" /> — a fresh pod is ready while it
///             is still working on its first registration (connect, token, controller rollout), so it
///             is never taken out of service before it ever had a chance.
///         </item>
///         <item>
///             <see cref="AdapterOptions.HubReadinessProbeEnabled" /> — an escape hatch for
///             deployments that must stay ready without a controller connection.
///         </item>
///     </list>
///     A brief reconnect is additionally absorbed by the probe's own <c>failureThreshold</c> in the
///     chart.
/// </remarks>
public abstract class HubRegistrationReadinessHealthCheck : IHealthCheck
{
    private readonly IOptions<AdapterOptions> _adapterOptions;
    private readonly string _hubName;
    private readonly IAdapterHubRegistrationState _registrationState;
    private readonly string _subject;

    /// <summary>
    ///     Constructor.
    /// </summary>
    /// <param name="registrationState">The registration state the process writes on every (re)registration.</param>
    /// <param name="adapterOptions">Probe switch and grace period.</param>
    /// <param name="subject">What is reported on, e.g. "Adapter".</param>
    /// <param name="hubName">The hub's name in the health check output.</param>
    protected HubRegistrationReadinessHealthCheck(IAdapterHubRegistrationState registrationState,
        IOptions<AdapterOptions> adapterOptions, string subject, string hubName)
    {
        _registrationState = registrationState;
        _adapterOptions = adapterOptions;
        _subject = subject;
        _hubName = hubName;
    }

    /// <summary>
    ///     Whether the connection to the hub is currently up. May throw; a throw counts as "not alive".
    /// </summary>
    protected abstract bool IsConnectionAlive { get; }

    /// <summary>
    ///     A reason this process is not ready regardless of its registration, or <c>null</c>. Checked
    ///     after the probe switch and before the registration (AB#5864: a draining pool member).
    /// </summary>
    protected virtual string? NotReadyReason => null;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _adapterOptions.Value;
        if (!options.HubReadinessProbeEnabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"{_subject} readiness gating on the {_hubName} is disabled by configuration (Adapter:HubReadinessProbeEnabled)."));
        }

        if (NotReadyReason is { } notReadyReason)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(notReadyReason));
        }

        var isAlive = ReadIsConnectionAlive();
        if (_registrationState.IsRegistered && isAlive)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"{_subject} is registered at the {_hubName} since {_registrationState.LastRegisteredUtc:O}."));
        }

        var sinceStart = DateTime.UtcNow - _registrationState.ProcessStartUtc;
        if (!_registrationState.HasEverRegistered && sinceStart < options.HubReadinessGracePeriod)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"{_subject} has not registered at the {_hubName} yet, still within the startup grace period of {options.HubReadinessGracePeriod}."));
        }

        var reason = _registrationState.LastFailureMessage ?? $"no registration at the {_hubName}";
        return Task.FromResult(HealthCheckResult.Unhealthy(
            $"{_subject} is not registered at the {_hubName} (connection alive: {isAlive}, "
            + $"last registration: {_registrationState.LastRegisteredUtc?.ToString("O") ?? "never"}). Reason: {reason}"));
    }

    private bool ReadIsConnectionAlive()
    {
        try
        {
            return IsConnectionAlive;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
