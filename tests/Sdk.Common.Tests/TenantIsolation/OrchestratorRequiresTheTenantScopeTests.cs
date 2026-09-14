using System.Runtime.CompilerServices;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Sdk.Common.Tests.TenantIsolation;

/// <summary>
///     AB#4924 increment 6, entry criterion 6 — the orchestrator <b>requires</b> a tenant scope.
/// </summary>
/// <remarks>
///     <para>
///         Increment 3 resolved <see cref="IAdapterTenantScope" /> with <c>GetService</c> so adapter
///         hosts that do not build on <c>AdapterBuilder</c> kept working unchanged while the refactor
///         baked across the fleet. 🔴 <b>That tolerance had to go before the first lease</b>: on a pool
///         member a missing scope means every execution silently runs with no tenant entered, which is
///         a confusing null at best and another tenant's data at worst — and it is exactly the class of
///         failure that looks fine in a log.
///     </para>
///     <para>
///         Removing it is only safe because the registration moved into <c>AddDataPipeline()</c>, the
///         same call that registers the orchestrator. The first test pins that; without it the change
///         would break every host that composes the pipeline without an adapter builder — the SAP
///         adapter, the Zenon Windows service, the two SDK samples and a dozen test fixtures across
///         the adapter repositories.
///     </para>
/// </remarks>
public class OrchestratorRequiresTheTenantScopeTests
{
    /// <summary>
    ///     🔴 The registration that makes <c>GetRequiredService</c> safe. Anything that can resolve an
    ///     <c>IEtlDataOrchestrator</c> must be able to resolve the scope it requires.
    /// </summary>
    [Fact]
    public void AddDataPipelineRegistersTheTenantScopeBesideTheOrchestrator()
    {
        var services = new ServiceCollection();
        services.AddDataPipeline();

        using var provider = services.BuildServiceProvider();

        var scope = provider.GetService<IAdapterTenantScope>();
        Assert.NotNull(scope);
        Assert.False(scope!.IsPoolMember);
    }

    /// <summary>
    ///     TryAdd, so a pool member that registered the lease-aware scope first is not overwritten by
    ///     the dedicated one. A pool member silently running the dedicated scope would enforce no
    ///     lease at all, and every execution would still look fine.
    /// </summary>
    [Fact]
    public void APreRegisteredPoolScopeSurvivesAddDataPipeline()
    {
        var services = new ServiceCollection();
        services.AddAdapterPoolMember();
        services.AddDataPipeline();

        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<IAdapterTenantScope>().IsPoolMember);
    }

    /// <summary>
    ///     The tolerance itself, pinned at the source. A silent revert to <c>GetService</c> would
    ///     compile, pass every existing test, and reintroduce exactly the failure mode above — there is
    ///     no behavioural assertion that can catch it, because the difference only shows on a host that
    ///     does not register the scope, which after the move is none of them.
    /// </summary>
    [Fact]
    public void TheOrchestratorResolvesTheScopeWithGetRequiredService()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(),
            "src", "Sdk.Pipeline", "EtlDataPipeline", "EtlDataOrchestrator.cs"));

        Assert.Contains("GetRequiredService<IAdapterTenantScope>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetService<IAdapterTenantScope>()", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));
    }
}
