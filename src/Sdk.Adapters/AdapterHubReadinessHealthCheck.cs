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
///         Two deliberate softeners keep this from becoming a hair trigger:
///         <list type="bullet">
///             <item>
///                 <see cref="AdapterOptions.HubReadinessGracePeriod" /> — a fresh pod is ready while
///                 it is still working on its first registration (connect, token, controller
///                 rollout), so it is never taken out of service before it ever had a chance.
///             </item>
///             <item>
///                 <see cref="AdapterOptions.HubReadinessProbeEnabled" /> — an escape hatch for
///                 deployments that must stay ready without a controller connection.
///             </item>
///         </list>
///         A brief reconnect is additionally absorbed by the probe's own
///         <c>failureThreshold</c> in the chart.
///     </para>
/// </remarks>
public sealed class AdapterHubReadinessHealthCheck(
    IAdapterHubClient adapterHubClient,
    IAdapterHubRegistrationState registrationState,
    IOptions<AdapterOptions> adapterOptions) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = adapterOptions.Value;
        if (!options.HubReadinessProbeEnabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Adapter hub readiness gating is disabled by configuration (Adapter:HubReadinessProbeEnabled)."));
        }

        if (registrationState.IsRegistered && adapterHubClient.IsAlive)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Adapter is registered at the communication hub since {registrationState.LastRegisteredUtc:O}."));
        }

        var sinceStart = DateTime.UtcNow - registrationState.ProcessStartUtc;
        if (!registrationState.HasEverRegistered && sinceStart < options.HubReadinessGracePeriod)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Adapter has not registered at the communication hub yet, still within the startup grace period of {options.HubReadinessGracePeriod}."));
        }

        var reason = registrationState.LastFailureMessage ?? "no registration at the adapter hub";
        return Task.FromResult(HealthCheckResult.Unhealthy(
            $"Adapter is not registered at the communication hub (connection alive: {adapterHubClient.IsAlive}, "
            + $"last registration: {registrationState.LastRegisteredUtc?.ToString("O") ?? "never"}). Reason: {reason}"));
    }
}
