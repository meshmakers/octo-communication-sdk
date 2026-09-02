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
///     <see cref="AdapterOptions.TenantId" />: it becomes <c>acr_values=tenant:{TenantId}</c> on the
///     token request, and dropping it hands the adapter a token for the <b>system</b> tenant
///     (AB#5077), which the controller then refuses on the adapter's own tenant route with a 403 —
///     a failure that reads like a broken gate and points nowhere near this mapping.
/// </remarks>
public sealed class ConfigureAdapterAuthenticatorOptions : IConfigureOptions<AuthenticatorOptions>
{
    private readonly IOptions<AdapterOptions> _adapterOptions;

    /// <summary>
    ///     Constructor.
    /// </summary>
    /// <param name="adapterOptions">The adapter options to project.</param>
    public ConfigureAdapterAuthenticatorOptions(IOptions<AdapterOptions> adapterOptions)
    {
        _adapterOptions = adapterOptions;
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
        // Drives acr_values=tenant:{TenantId} on the token request. Without it the identity service
        // issues for the system tenant since AB#5077 and the adapter is refused on its own route.
        options.TenantId = adapterOptions.TenantId;
    }
}
