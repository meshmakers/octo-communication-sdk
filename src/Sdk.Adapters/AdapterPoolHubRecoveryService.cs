using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     The pool member's counterpart of <see cref="AdapterHubRecoveryService" /> (AB#4924 AP-I5): a
///     member that <i>was</i> registered at the adapter pool hub and stays unregistered for longer
///     than <see cref="AdapterOptions.HubRegistrationRecoveryTimeout" /> stops its host so the
///     container restarts.
/// </summary>
/// <remarks>
///     <para>
///         A deaf member is worse than a deaf dedicated adapter in one respect: nothing it already
///         knows keeps running. A member has no pipelines of its own between leases, so a member the
///         controller cannot reach does <b>nothing at all</b> — it is simply never leased, while the
///         pod stays <c>1/1 Running</c> and the borrowers' queues grow. The SignalR client reconnects
///         and <see cref="AdapterPoolMemberService" /> re-registers on every connect and retries a
///         refused registration on every heartbeat tick; this service is the backstop when neither
///         gets it back.
///     </para>
///     <para>
///         A lease that is still running when the timeout fires is lost with the process. That is
///         consistent with the controller's view: it interrupted and re-queued that lease the moment
///         the member's connection went away (concept §6, at-least-once).
///     </para>
/// </remarks>
public sealed class AdapterPoolHubRecoveryService(
    IAdapterPoolHubClient adapterPoolHubClient,
    IAdapterHubRegistrationState registrationState,
    IOptions<AdapterOptions> adapterOptions,
    AdapterLifetimeManagement lifetimeManagement)
    : HubRegistrationRecoveryService(registrationState, adapterOptions, lifetimeManagement, "adapter pool hub")
{
    /// <inheritdoc />
    protected override bool IsConnectionAlive => adapterPoolHubClient.IsAlive;
}
