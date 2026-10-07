using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5865 — the pool hub connection presents the member's own (lending pool) identity, never the
///     borrower's lease identity.
/// </summary>
/// <remarks>
///     test-2-dev, 2026-10-07: the controller restarted while a lease was running, the member's pool hub
///     connection was rebuilt during the lease, and the SignalR <c>AccessTokenProvider</c> — which reads
///     the client's token holder on every (re)connect — returned the borrower's token that
///     <c>BorrowerIdentityLeaseParticipant</c> had written into the process-wide holder. The deferred
///     re-registration after the lease went out on that connection as tenant 'leasetest' for a pool of
///     tenant 'meshdev'.
/// </remarks>
public class AdapterPoolHubIdentityTests
{
    private static ServiceProvider BuildMember(bool withTokenService = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAdapterPoolMember();
        if (withTokenService)
        {
            services.AddOptions<AdapterOptions>().Configure(o =>
            {
                o.IssuerUri = "https://connect.test-2.mm.cloud";
                o.ClientId = "pool-member";
                o.ClientSecret = "s3cr3t";
            });
            services.AddSingleton(A.Fake<IAuthenticatorClient>());
            services.AddAdapterAccessTokenService(isPoolMember: true);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ThePoolHubClientReadsItsOwnHolder_NotTheProcessWideLeaseIdentity()
    {
        await using var provider = BuildMember();

        var hubClient = (AdapterPoolHubClient)provider.GetRequiredService<IAdapterPoolHubClient>();

        Assert.Same(provider.GetRequiredService<AdapterPoolHubAccessToken>(), hubClient.ClientAccessToken);
        Assert.NotSame(provider.GetRequiredService<IServiceClientAccessToken>(), hubClient.ClientAccessToken);
    }

    [Fact]
    public async Task ABorrowerTokenWrittenDuringALease_NeverReachesThePoolHubConnection()
    {
        await using var provider = BuildMember();
        var hubClient = (AdapterPoolHubClient)provider.GetRequiredService<IAdapterPoolHubClient>();
        var leaseIdentity = provider.GetRequiredService<IServiceClientAccessToken>();
        provider.GetRequiredService<AdapterPoolHubAccessToken>().AccessToken = "lender-pool-token";

        // Enter: what BorrowerIdentityLeaseParticipant does. A reconnect now reads the hub's holder.
        leaseIdentity.AccessToken = "borrower-token";
        Assert.Equal("lender-pool-token", hubClient.ClientAccessToken.AccessToken);

        // Leave: the lease identity is emptied; the member's own identity survives.
        leaseIdentity.AccessToken = null;
        Assert.Equal("lender-pool-token", hubClient.ClientAccessToken.AccessToken);
    }

    [Fact]
    public async Task TheMembersOwnCredential_IsWrittenToThePoolHubHolder_AndLeavesTheLeaseIdentityAlone()
    {
        await using var provider = BuildMember(withTokenService: true);
        A.CallTo(() => provider.GetRequiredService<IAuthenticatorClient>().RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .Returns(new AuthenticationData { AccessToken = "lender-pool-token", ExpiresAt = DateTime.Now.AddHours(1) });
        var leaseIdentity = provider.GetRequiredService<IServiceClientAccessToken>();
        leaseIdentity.AccessToken = "borrower-token";

        var tokenService = provider.GetRequiredService<AdapterAccessTokenService>();
        Assert.True(await tokenService.EnsureTokenAsync());

        Assert.Equal("lender-pool-token", provider.GetRequiredService<AdapterPoolHubAccessToken>().AccessToken);
        // 🔴 The refresh must not overwrite a running lease's borrower identity.
        Assert.Equal("borrower-token", leaseIdentity.AccessToken);
        Assert.Contains(provider.GetServices<IHostedService>(), s => ReferenceEquals(s, tokenService));
    }

    [Fact]
    public async Task ADedicatedAdapter_KeepsWritingItsOwnCredentialIntoTheProcessWideHolder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IServiceClientAccessToken, Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants.ServiceClientAccessToken>();
        services.AddOptions<AdapterOptions>().Configure(o =>
        {
            o.DedicatedTenantId = "meshdev";
            o.IssuerUri = "https://connect.test-2.mm.cloud";
            o.ClientId = "adapter";
            o.ClientSecret = "s3cr3t";
        });
        var authenticator = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticator.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .Returns(new AuthenticationData { AccessToken = "adapter-token", ExpiresAt = DateTime.Now.AddHours(1) });
        services.AddSingleton(authenticator);
        services.AddAdapterAccessTokenService(isPoolMember: false);
        await using var provider = services.BuildServiceProvider();

        Assert.True(await provider.GetRequiredService<AdapterAccessTokenService>().EnsureTokenAsync());

        Assert.Equal("adapter-token", provider.GetRequiredService<IServiceClientAccessToken>().AccessToken);
    }
}
