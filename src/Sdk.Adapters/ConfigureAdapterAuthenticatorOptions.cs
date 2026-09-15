using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Projects the adapter's own client-credentials configuration from <see cref="AdapterOptions" />
///     onto the SDK's <see cref="AuthenticatorOptions" />, which is what
///     <see cref="AuthenticatorClient" /> reads (AB#5072).
/// </summary>
/// <remarks>
///     An <see cref="IConfigureOptions{TOptions}" /> rather than an inline delegate in the adapter
///     builders so the projection can be pinned by a test, and so both
///     <c>AdapterBuilder</c> and <c>WebAdapterBuilder</c> share one definition instead of two copies
///     that can drift. The field that makes that worth doing is
///     <see cref="AdapterOptions.DedicatedTenantId" />: it becomes <c>acr_values=tenant:{TenantId}</c> on the
///     token request, and dropping it hands the adapter a token for the <b>system</b> tenant
///     (AB#5077), which the controller then refuses on the adapter's own tenant route with a 403 —
///     a failure that reads like a broken gate and points nowhere near this mapping.
/// </remarks>
public sealed class ConfigureAdapterAuthenticatorOptions : IConfigureOptions<AuthenticatorOptions>
{
    private readonly IOptions<AdapterOptions> _adapterOptions;
    private readonly IOptions<AdapterPoolMemberOptions> _poolMemberOptions;

    /// <summary>
    ///     Constructor.
    /// </summary>
    /// <param name="adapterOptions">The adapter options to project.</param>
    /// <param name="poolMemberOptions">The pool-member options; see <see cref="Configure" />.</param>
    public ConfigureAdapterAuthenticatorOptions(IOptions<AdapterOptions> adapterOptions,
        IOptions<AdapterPoolMemberOptions> poolMemberOptions)
    {
        _adapterOptions = adapterOptions;
        _poolMemberOptions = poolMemberOptions;
    }

    /// <inheritdoc />
    public void Configure(AuthenticatorOptions options)
    {
        var adapterOptions = _adapterOptions.Value;

        // Empty rather than null: AuthorizationClient builds its discovery cache only for a
        // non-blank IssuerUri, so an unconfigured adapter still constructs the client without
        // throwing — and AdapterAccessTokenService never calls it.
        options.IssuerUri = adapterOptions.IssuerUri ?? string.Empty;
        options.ClientId = adapterOptions.ClientId ?? string.Empty;
        options.ClientSecret = adapterOptions.ClientSecret;
        options.AdditionalValidIssuers = adapterOptions.AdditionalValidIssuers;
        // Drives acr_values=tenant:{TenantId} on the token request. Without it the identity service
        // issues for the system tenant since AB#5077 and the adapter is refused on its own route.
        // This is the adapter's OWN credential for its OWN connection — never the tenant of an
        // execution, which arrives per lease (AB#4924 concept §4, Q6).
        //
        // 🔴 A pool member has a connection tenant too, and it is the LENDER. An earlier comment
        // here claimed a member "has none"; that was wrong and cost a diagnosis during the first
        // end-to-end lease run. The management connection is authorized against the lending tenant
        // (concept §8, Q4), so without a tenant the constructor default applies, the member
        // registers under the wrong one, and staged LogOnly authorization accepts it silently.
        //
        // Derived from the pool options rather than requiring DedicatedTenantId to be set as well:
        // two settings that must agree are two settings that can disagree, and this keeps
        // DedicatedTenantId null on a member — which is what stops execution-path code from finding
        // a process-wide tenant to read (increment 3).
        var poolMember = _poolMemberOptions.Value;
        options.TenantId = poolMember.IsEnabled
            ? poolMember.PoolTenantId
            : adapterOptions.DedicatedTenantId;
    }
}
