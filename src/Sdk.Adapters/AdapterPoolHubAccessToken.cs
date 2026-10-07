using Meshmakers.Octo.Sdk.ServiceClient;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     The access token a pool member presents on its <b>own</b> management connection to the
///     controller's <c>/adapterPoolHub</c> — the lending pool's identity, never a borrower's (AB#5865).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Deliberately not the process-wide <see cref="IServiceClientAccessToken" />.</b> During a
///         lease the borrower-identity participant writes the BORROWER's token into that holder, because
///         every service client and pipeline node of the leased execution must act as the borrower. The
///         pool hub client used to read the same holder through its <c>AccessTokenProvider</c>, which is
///         called on every (re)connect: a connection rebuilt while a lease was running (controller
///         restart, network blip) was authenticated as the borrower, and the deferred re-registration
///         after the lease went out on that connection. The controller logged "presents a token of tenant
///         'leasetest' but claims to belong to a pool of tenant 'meshdev'" and would refuse it under
///         <c>AdapterPoolHubAuthorization:Mode=Enforce</c> (test-2-dev, 2026-10-07).
///     </para>
///     <para>
///         Separate holders also stop the two writers from clobbering each other the other way round:
///         the member's own credential refresh (<see cref="AdapterAccessTokenService" />) no longer
///         overwrites a borrower token mid-lease, and leaving a lease no longer wipes the member's own
///         token.
///     </para>
///     <para>
///         Written by <see cref="AdapterAccessTokenService" /> on a pool member (the member's own
///         client credential, tenant = lending pool's tenant); read only by the pool hub client. Empty when
///         the member has no own client credential — exactly what the connection presented before
///         outside a lease.
///     </para>
/// </remarks>
public sealed class AdapterPoolHubAccessToken : IServiceClientAccessToken
{
    private string? _accessToken;

    /// <inheritdoc />
    public event EventHandler? AccessTokenUpdated;

    /// <inheritdoc />
    public string? AccessToken
    {
        get => _accessToken;
        set
        {
            if (_accessToken != value)
            {
                _accessToken = value;
                AccessTokenUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
