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
    private static AuthenticatorOptions Project(AdapterOptions adapterOptions)
    {
        var authenticatorOptions = new AuthenticatorOptions();
        new ConfigureAdapterAuthenticatorOptions(Options.Create(adapterOptions)).Configure(authenticatorOptions);
        return authenticatorOptions;
    }

    [Fact]
    public void TheAdapterTenantBecomesTheAcrValuesTenant()
    {
        var options = Project(new AdapterOptions
        {
            TenantId = "acmeTenant",
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
            TenantId = "acmeTenant",
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
        var options = Project(new AdapterOptions { TenantId = "acmeTenant" });

        Assert.Equal(string.Empty, options.IssuerUri);
        Assert.Equal(string.Empty, options.ClientId);
        Assert.Null(options.ClientSecret);
    }
}
