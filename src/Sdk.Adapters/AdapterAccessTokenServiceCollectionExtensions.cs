using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Registers the adapter's own credential refresh (AB#5072) into the holder of the connection it
///     belongs to (AB#5865). Shared by <c>AdapterBuilder</c> and <c>WebAdapterBuilder</c>.
/// </summary>
public static class AdapterAccessTokenServiceCollectionExtensions
{
    /// <summary>
    ///     Adds <see cref="AdapterAccessTokenService" /> as a singleton and hosted service.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A dedicated adapter writes its own token into the process-wide
    ///         <see cref="IServiceClientAccessToken" />, which its adapter hub connection and every
    ///         service client read — one identity for the whole process.
    ///     </para>
    ///     <para>
    ///         🔴 A pool member has two identities and therefore two holders: its own (the lending pool's
    ///         tenant), which only the pool hub connection presents, and the lease identity in the
    ///         process-wide holder (the borrower's token during a lease, empty between leases). The
    ///         own credential goes into <see cref="AdapterPoolHubAccessToken" />. With one shared holder
    ///         a hub reconnect during a lease authenticated the management connection as the borrower,
    ///         and this refresh could overwrite a borrower token in the middle of a lease.
    ///     </para>
    ///     <para>
    ///         Must be called before the hosted services that open a hub connection: hosted services
    ///         start sequentially and the first connection should already carry a token.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddAdapterAccessTokenService(this IServiceCollection services,
        bool isPoolMember)
    {
        services.TryAddSingleton<AdapterPoolHubAccessToken>();
        services.AddSingleton(provider => new AdapterAccessTokenService(
            provider.GetRequiredService<ILogger<AdapterAccessTokenService>>(),
            provider.GetRequiredService<IOptions<AdapterOptions>>(),
            isPoolMember
                ? provider.GetRequiredService<AdapterPoolHubAccessToken>()
                : provider.GetRequiredService<IServiceClientAccessToken>(),
            provider.GetRequiredService<IAuthenticatorClient>()));
        services.AddHostedService(provider => provider.GetRequiredService<AdapterAccessTokenService>());
        return services;
    }
}
