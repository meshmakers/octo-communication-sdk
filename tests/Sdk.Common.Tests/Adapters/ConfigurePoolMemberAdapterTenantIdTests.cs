using Meshmakers.Octo.Sdk.Common.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#4924 — a pool member carries no dedicated tenant.
/// </summary>
/// <remarks>
///     🔴 These pin a sentence three call sites in two repositories rely on and that was false until
///     <see cref="ConfigurePoolMemberAdapterTenantId" /> existed: <c>AdapterOptions</c>' constructor
///     default reaches a member like any other process, and the local runbook told the operator to
///     set the key to the <b>lending</b> tenant. The sharpest consequence is
///     <c>ServiceAccountTokenService.ResolveTenantId</c> in <c>octo-mesh-adapter</c>, which falls back
///     to this value when a service-account configuration names no tenant — with a value present, a
///     borrower's execution would have acquired a token for the lender instead of declining.
/// </remarks>
public class ConfigurePoolMemberAdapterTenantIdTests
{
    private static AdapterOptions PostConfigure(AdapterOptions adapterOptions,
        AdapterPoolMemberOptions poolMemberOptions)
    {
        new ConfigurePoolMemberAdapterTenantId(Options.Create(poolMemberOptions),
                NullLogger<ConfigurePoolMemberAdapterTenantId>.Instance)
            .PostConfigure(Options.DefaultName, adapterOptions);
        return adapterOptions;
    }

    private static AdapterPoolMemberOptions ConfiguredPool() =>
        new() { AdapterPoolTenantId = "lender", AdapterPoolRtId = "49240000000000000000aa01" };

    /// <summary>
    ///     The configuration the old runbook produced: the key set to the lending tenant. Clearing it
    ///     is the whole point — a member executes only for the tenant of the lease it holds.
    /// </summary>
    [Fact]
    public void AMemberDropsAnExplicitlyConfiguredTenant()
    {
        var options = PostConfigure(new AdapterOptions { DedicatedTenantId = "lender" }, ConfiguredPool());

        Assert.Null(options.DedicatedTenantId);
    }

    /// <summary>
    ///     The commoner case, and the one nobody would look for: nothing was configured at all, so the
    ///     constructor default applied and the member carried <c>"meshTest"</c>.
    /// </summary>
    [Fact]
    public void AMemberDropsTheConstructorDefault()
    {
        var options = PostConfigure(new AdapterOptions(), ConfiguredPool());

        Assert.Null(options.DedicatedTenantId);
    }

    /// <summary>
    ///     <c>IsEnabled</c> needs both ids, so half a pool configuration is not a pool member. A typo in
    ///     one variable must not silently un-tenant a dedicated adapter — that would take its hub route
    ///     and its own credential with it.
    /// </summary>
    [Fact]
    public void AnIncompletePoolConfigurationLeavesTheDedicatedTenantInPlace()
    {
        var options = PostConfigure(new AdapterOptions { DedicatedTenantId = "acmeTenant" },
            new AdapterPoolMemberOptions { AdapterPoolTenantId = "lender" }); // AdapterPoolRtId missing

        Assert.Equal("acmeTenant", options.DedicatedTenantId);
    }

    [Fact]
    public void AProcessWithNoPoolConfigurationIsUntouched()
    {
        var options = PostConfigure(new AdapterOptions { DedicatedTenantId = "acmeTenant" },
            new AdapterPoolMemberOptions());

        Assert.Equal("acmeTenant", options.DedicatedTenantId);
    }

    /// <summary>
    ///     Through the real composition rather than by calling the type directly: what matters is that
    ///     <c>AddAdapterPoolMember()</c> registers it, because a member is composed by that one call
    ///     and nothing else would notice its absence.
    /// </summary>
    [Fact]
    public void TheRegistrationIsPartOfThePoolMemberComposition()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdapterPoolMember();
        services.Configure<AdapterPoolMemberOptions>(options =>
        {
            options.AdapterPoolTenantId = "lender";
            options.AdapterPoolRtId = "49240000000000000000aa01";
        });
        services.Configure<AdapterOptions>(options => options.DedicatedTenantId = "lender");

        var adapterOptions = services.BuildServiceProvider().GetRequiredService<IOptions<AdapterOptions>>();

        Assert.Null(adapterOptions.Value.DedicatedTenantId);
    }

    /// <summary>
    ///     The member's own connection credential must survive the clearing — it is derived from the
    ///     pool options, not from the property this type empties, and losing it registers the member
    ///     under the wrong tenant (which staged <c>LogOnly</c> authorization accepts silently).
    /// </summary>
    [Fact]
    public void TheMembersOwnCredentialStillNamesTheLender()
    {
        var poolMemberOptions = ConfiguredPool();
        var adapterOptions = PostConfigure(new AdapterOptions { ClientId = "octo-mesh-adapter" },
            poolMemberOptions);

        var authenticatorOptions = new Meshmakers.Octo.Sdk.ServiceClient.Authentication.AuthenticatorOptions();
        new ConfigureAdapterAuthenticatorOptions(Options.Create(adapterOptions),
            Options.Create(poolMemberOptions)).Configure(authenticatorOptions);

        Assert.Equal("lender", authenticatorOptions.TenantId);
    }
}
