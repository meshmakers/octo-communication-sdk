using Meshmakers.Octo.Sdk.Common.Adapters;
using Microsoft.Extensions.Configuration;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5303 item 5 — a process told to be a pool member must fail rather than degrade into an
///     ordinary adapter.
/// </summary>
/// <remarks>
///     <para>
///         🔴 The case these are written around is not a missing setting but a <b>mis-spelled</b> one,
///         and that is why the guard keys on the section rather than on the properties. The chart
///         emitted <c>OCTO_ADAPTERPOOL__POOLTENANTID</c> while the SDK binds
///         <c>AdapterPoolTenantId</c>: every key arrived, none of them bound, <c>IsEnabled</c> read
///         false, and the pod came up <c>1/1 Ready</c> as a plain adapter. No assertion on the
///         properties could see that — they look exactly like a dedicated adapter's.
///     </para>
/// </remarks>
public class AdapterPoolMemberConfigurationGuardTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(
                e => $"{AdapterPoolMemberOptions.SectionName}:{e.Key}", e => e.Value))
            .Build();
    }

    /// <summary>The bug as it was observed: the old env spelling, which binds nothing.</summary>
    [Fact]
    public void ASectionSpelledTheOldWayIsRefusedInsteadOfIgnored()
    {
        var configuration = Configuration(
            ("PoolTenantId", "accounting"),
            ("PoolRtId", "49240000000000000000aa01"));

        var exception = Assert.Throws<AdapterException>(
            () => AdapterPoolMemberConfigurationGuard.BindAndVerify(configuration));

        // Both halves have to be in the message, or it does not shorten the next diagnosis: what
        // arrived, and what the SDK was looking for.
        Assert.Contains("PoolTenantId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OCTO_ADAPTERPOOL__ADAPTERPOOLTENANTID", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OCTO_ADAPTERPOOL__ADAPTERPOOLRTID", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Half a configuration is the other silent path: the chart renders both or neither, so one
    ///     id alone means something rewrote the pod's environment.
    /// </summary>
    [Fact]
    public void HalfAConfigurationIsRefusedRatherThanRenderedAsADedicatedAdapter()
    {
        var configuration = Configuration(("AdapterPoolTenantId", "accounting"));

        var exception = Assert.Throws<AdapterException>(
            () => AdapterPoolMemberConfigurationGuard.BindAndVerify(configuration));

        Assert.Contains("AdapterPoolRtId", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("OCTO_ADAPTERPOOL__ADAPTERPOOLTENANTID", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A correctly configured member passes and is enabled.</summary>
    [Fact]
    public void AConfiguredMemberBindsAndIsEnabled()
    {
        var configuration = Configuration(
            ("AdapterPoolTenantId", "accounting"),
            ("AdapterPoolRtId", "49240000000000000000aa01"));

        var options = AdapterPoolMemberConfigurationGuard.BindAndVerify(configuration);

        Assert.True(options.IsEnabled);
        Assert.Equal("accounting", options.AdapterPoolTenantId);
        Assert.Equal("49240000000000000000aa01", options.AdapterPoolRtId);
    }

    /// <summary>
    ///     🔴 The guard must not fire on the overwhelmingly common case. Every dedicated adapter in
    ///     the fleet has no <c>AdapterPool</c> section at all, and a guard that threw on them would
    ///     take the fleet down rather than one broken member.
    /// </summary>
    [Fact]
    public void NoSectionStaysAPlainAdapter()
    {
        var options = AdapterPoolMemberConfigurationGuard.BindAndVerify(new ConfigurationBuilder().Build());

        Assert.False(options.IsEnabled);
    }

    /// <summary>
    ///     An empty or whitespace value is not a claim. values.yaml ships
    ///     <c>adapterPool.poolTenantId: ""</c>, and a host that binds it must stay a plain adapter —
    ///     the same rule <see cref="AdapterPoolMemberOptions.IsEnabled" /> already applies.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySectionValueIsNotAClaimToBeAMember(string value)
    {
        var configuration = Configuration(
            ("AdapterPoolTenantId", value),
            ("AdapterPoolRtId", value));

        var options = AdapterPoolMemberConfigurationGuard.BindAndVerify(configuration);

        Assert.False(options.IsEnabled);
    }

    /// <summary>
    ///     A member id on its own configures no member. It is refused rather than ignored: the chart
    ///     deliberately never sets it (<c>EffectiveMemberId</c> falls back to the pod name), so its
    ///     presence without the ids means something else wrote that environment.
    /// </summary>
    [Fact]
    public void AMemberIdWithoutThePoolIdsIsRefused()
    {
        var configuration = Configuration(("MemberId", "octo-pool-0"));

        Assert.Throws<AdapterException>(
            () => AdapterPoolMemberConfigurationGuard.BindAndVerify(configuration));
    }

    /// <summary>The environment variable names in the message are derived, not typed out twice.</summary>
    [Fact]
    public void TheEnvironmentVariableNameFollowsTheSectionAndProperty()
    {
        Assert.Equal("OCTO_ADAPTERPOOL__ADAPTERPOOLRTID",
            AdapterPoolMemberConfigurationGuard.EnvironmentVariableFor(
                nameof(AdapterPoolMemberOptions.AdapterPoolRtId)));
    }
}
