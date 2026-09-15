using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5072 — the projection from <see cref="AdapterOptions" /> onto the SDK's
///     <see cref="AuthenticatorOptions" />. Pinned because losing <c>TenantId</c> here produces a
///     token for the system tenant (AB#5077) and a 403 on the adapter's own tenant route — a failure
///     that points nowhere near this mapping.
/// </summary>
public class ConfigureAdapterAuthenticatorOptionsTests
{
    private static AuthenticatorOptions Project(AdapterOptions adapterOptions,
        AdapterPoolMemberOptions? poolMemberOptions = null)
    {
        var authenticatorOptions = new AuthenticatorOptions();
        new ConfigureAdapterAuthenticatorOptions(Options.Create(adapterOptions),
                Options.Create(poolMemberOptions ?? new AdapterPoolMemberOptions()))
            .Configure(authenticatorOptions);
        return authenticatorOptions;
    }

    /// <summary>
    ///     🔴 AB#4924 — a pool member authenticates as the <b>lending</b> tenant, because its
    ///     management connection is authorized against that tenant (concept §8, Q4). The earlier
    ///     comment here claimed a member had no connection tenant at all; it registered under the
    ///     constructor default instead, and staged LogOnly authorization accepted it silently.
    /// </summary>
    [Fact]
    public void APoolMemberAuthenticatesAsTheLendingTenant()
    {
        var options = Project(new AdapterOptions
            {
                IssuerUri = "https://connect.test-2.mm.cloud",
                ClientId = "octo-mesh-adapter",
                ClientSecret = "secret"
                // No DedicatedTenantId: a member has no tenant of its own to execute for.
            },
            new AdapterPoolMemberOptions
            {
                PoolTenantId = "lender",
                PoolRtId = "665f0000000000000000ee21"
            });

        Assert.Equal("lender", options.TenantId);
    }

    /// <summary>
    ///     Half a pool configuration is not a pool member (<c>IsEnabled</c> needs both ids), so the
    ///     dedicated path must still apply — otherwise a typo in one variable would silently
    ///     re-point a dedicated adapter's credential.
    /// </summary>
    [Fact]
    public void AnIncompletePoolConfigurationLeavesTheDedicatedTenantInPlace()
    {
        var options = Project(new AdapterOptions
            {
                DedicatedTenantId = "acmeTenant",
                IssuerUri = "https://connect.test-2.mm.cloud",
                ClientId = "octo-mesh-adapter",
                ClientSecret = "secret"
            },
            new AdapterPoolMemberOptions { PoolTenantId = "lender" });   // PoolRtId missing

        Assert.Equal("acmeTenant", options.TenantId);
    }

    [Fact]
    public void TheAdapterTenantBecomesTheAcrValuesTenant()
    {
        var options = Project(new AdapterOptions
        {
            DedicatedTenantId = "acmeTenant",
            IssuerUri = "https://connect.test-2.mm.cloud",
            ClientId = "octo-mesh-adapter",
            ClientSecret = "secret"
        });

        Assert.Equal("acmeTenant", options.TenantId);
    }

    [Fact]
    public void IssuerClientIdAndSecretAreProjected()
    {
        var options = Project(new AdapterOptions
        {
            DedicatedTenantId = "acmeTenant",
            IssuerUri = "https://connect.test-2.mm.cloud",
            ClientId = "octo-mesh-adapter",
            ClientSecret = "secret"
        });

        Assert.Equal("https://connect.test-2.mm.cloud", options.IssuerUri);
        Assert.Equal("octo-mesh-adapter", options.ClientId);
        Assert.Equal("secret", options.ClientSecret);
    }

    [Fact]
    public void AnUnconfiguredAdapterProjectsEmptyStringsRatherThanNull()
    {
        // AuthorizationClient builds its discovery cache only for a non-blank IssuerUri, so the
        // client must still be constructible on an adapter that was given no credentials at all —
        // AdapterAccessTokenService simply never calls it.
        var options = Project(new AdapterOptions { DedicatedTenantId = "acmeTenant" });

        Assert.Equal(string.Empty, options.IssuerUri);
        Assert.Equal(string.Empty, options.ClientId);
        Assert.Null(options.ClientSecret);
    }
}
