using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Last line of defence for AB#5409: if a dedicated adapter that <i>was</i> registered at the
///     communication controller's adapter hub stays unregistered for longer than
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
///         The decision, its guards and its configuration are shared with the pool member's
///         counterpart, <see cref="AdapterPoolHubRecoveryService" />; see
///         <see cref="HubRegistrationRecoveryService" />.
///     </para>
/// </remarks>
public class AdapterHubRecoveryService(
    IAdapterHubClient adapterHubClient,
    IAdapterHubRegistrationState registrationState,
    IOptions<AdapterOptions> adapterOptions,
    AdapterLifetimeManagement lifetimeManagement)
    : HubRegistrationRecoveryService(registrationState, adapterOptions, lifetimeManagement, "adapter hub")
{
    /// <inheritdoc />
    protected override bool IsConnectionAlive => adapterHubClient.IsAlive;
}
