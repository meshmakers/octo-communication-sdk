using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#4924 — that <c>AddAdapterPoolMember()</c> produces a graph that can actually be
///     <b>resolved</b>, and options that are actually <b>bound</b>.
/// </summary>
/// <remarks>
///     <para>
///         🔴 These exist because the first end-to-end lease run found two defects that every
///         existing test missed, and missed for the same reason: the suites constructed
///         <c>AdapterPoolClient</c> by hand, and the DI sweep <i>enumerated</i> registrations
///         without resolving them. A registration list can be perfect while the graph it describes
///         cannot be built.
///     </para>
///     <para>
///         The cycle did not throw. Microsoft DI detects cycles by walking constructor parameters
///         and cannot see through a factory lambda, and <c>StackGuard.RunOnEmptyStack</c> kept
///         moving the recursion onto fresh stacks so it never overflowed either. The member printed
///         two lines and went silent — which is why the resolution test below is written with a
///         timeout rather than an assertion on an exception: the failure mode is a hang, and a test
///         that waited for a throw would have hung with it.
///     </para>
/// </remarks>
public class AdapterPoolMemberCompositionTests
{
    private static ServiceProvider BuildProvider(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddAdapterPoolMember();
        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     🔴 Every resolution in this class goes through a deadline, and that is not caution for its
    ///     own sake: an unbroken cycle never throws and never overflows, so a plain
    ///     <c>GetRequiredService</c> would hang the whole test process rather than fail one test.
    ///     A hanging suite reports nothing — the same lesson a guard test in this repo already had
    ///     to learn once.
    /// </summary>
    private static async Task<T> ResolveWithDeadline<T>(IServiceProvider provider) where T : notnull
    {
        var resolution = Task.Run(provider.GetRequiredService<T>);
        var finished = await Task.WhenAny(resolution,
            Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.True(ReferenceEquals(finished, resolution),
            $"Resolving {typeof(T).Name} did not finish within 10s — the dependency cycle is back. " +
            "It does not throw and does not overflow; it simply never returns.");

        return await resolution;
    }

    /// <summary>Resolves the three services that formed the cycle.</summary>
    [Fact]
    public async Task TheMemberGraphResolvesInsteadOfRecursingForEver()
    {
        await using var provider = BuildProvider();

        Assert.NotNull(await ResolveWithDeadline<AdapterPoolClient>(provider));
        Assert.NotNull(await ResolveWithDeadline<IAdapterPoolHubClient>(provider));
        Assert.NotNull(await ResolveWithDeadline<IAdapterPoolHubCallbacks>(provider));
    }

    /// <summary>
    ///     The callbacks registration must not be the client itself, or the cycle re-forms. The
    ///     forwarder resolves the client per call instead.
    /// </summary>
    [Fact]
    public async Task TheCallbacksTargetIsAForwarderRatherThanTheClientItself()
    {
        await using var provider = BuildProvider();

        var callbacks = await ResolveWithDeadline<IAdapterPoolHubCallbacks>(provider);
        var client = await ResolveWithDeadline<AdapterPoolClient>(provider);

        Assert.NotSame(client, callbacks);
    }

    /// <summary>
    ///     🔴 The options must be bound from configuration, not merely registered. Unbound, every
    ///     value stays at its default, <c>IsEnabled</c> is false, and the hosted service reports
    ///     "started without a configured pool" on a process that was configured correctly — the
    ///     second defect the end-to-end run exposed.
    /// </summary>
    [Fact]
    public async Task ThePoolSectionIsBoundFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AdapterPoolMemberOptions.SectionName}:PoolTenantId"] = "lender",
                [$"{AdapterPoolMemberOptions.SectionName}:PoolRtId"] = "665f0000000000000000ee21",
                [$"{AdapterPoolMemberOptions.SectionName}:MemberId"] = "octo-pool-0"
            })
            .Build();

        await using var provider = BuildProvider(configuration);

        var options = provider.GetRequiredService<IOptions<AdapterPoolMemberOptions>>().Value;

        Assert.Equal("lender", options.PoolTenantId);
        Assert.Equal("665f0000000000000000ee21", options.PoolRtId);
        Assert.Equal("octo-pool-0", options.MemberId);
        Assert.True(options.IsEnabled);
    }

    /// <summary>Without the section, the member stays off rather than half-configured.</summary>
    [Fact]
    public async Task WithoutTheSectionTheMemberIsNotEnabled()
    {
        await using var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<AdapterPoolMemberOptions>>().Value;

        Assert.False(options.IsEnabled);
        Assert.Null(options.PoolTenantId);
    }
}
