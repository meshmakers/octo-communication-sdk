using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Turns an adapter host into an adapter <b>pool member</b> (AB#4924, increment 6).
/// </summary>
public static class AdapterPoolServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the lease-aware tenant scope, the management connection and the pool client.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         🔴 <b>Call this before <c>AddDataPipeline()</c>.</b> The pipeline registration uses
    ///         <c>TryAddSingleton</c> for <see cref="IAdapterTenantScope" />, so whichever of the two
    ///         runs first wins — and for a pool member the winner must be
    ///         <see cref="AdapterPoolTenantScope" />. Registering the dedicated scope in a pool member
    ///         would not fail anywhere: it would simply never enforce the lease, and every execution
    ///         would look fine.
    ///     </para>
    ///     <para>
    ///         <b>Being a pool member is a property of the image, not of a flag.</b> This is an
    ///         explicit call in a host's composition root rather than a bindable switch, for the same
    ///         reason <c>AdapterTenantScope.IsPoolMember</c> is hard-coded false: a process that was
    ///         not built as a pool member must not be able to become one by environment variable.
    ///     </para>
    ///     <para>
    ///         Participants (<see cref="IAdapterLeaseParticipant" />) are added by the host on top —
    ///         the SDK cannot see the caches an adapter repository owns, and that is exactly where the
    ///         tenant-scoped state lives.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddAdapterPoolMember(this IServiceCollection services)
    {
        // 🔴 Bound, not merely registered. Without the bind the options object stays at its
        // defaults, IsEnabled is always false, and AdapterPoolMemberService logs "started without a
        // configured pool … Doing nothing" on a process that was configured correctly. Every host
        // binds the section into a LOCAL instance to decide whether to compose a member at all;
        // none of them bound it into DI, so the running service never saw it.
        //
        // 🔴 GetService, not Configure<IConfiguration>. The first attempt at this fix took
        // IConfiguration as a hard dependency of the options configuration, which made
        // AddAdapterPoolMember() unusable in any composition that does not register one — and broke
        // twelve leasing integration tests in octo-mesh-adapter, in a repo the change never
        // touched. A DI extension must compose in a bare ServiceCollection; a host without
        // configuration simply gets the defaults, which is exactly what it got before.
        services.AddOptions<AdapterPoolMemberOptions>()
            .Configure<IServiceProvider>((options, serviceProvider) =>
                serviceProvider.GetService<IConfiguration>()
                    ?.GetSection(AdapterPoolMemberOptions.SectionName)
                    .Bind(options));

        // 🔴 The counterpart of the binding above: with the pool configured, this process must not
        // also carry a dedicated tenant. AdapterOptions' constructor default made "it is null on a
        // pool member" false at all three call sites that rely on it, and the runbook made it worse
        // by naming the lender. See ConfigurePoolMemberAdapterTenantId.
        services.AddSingleton<IPostConfigureOptions<AdapterOptions>, ConfigurePoolMemberAdapterTenantId>();

        // Both interfaces, one instance: the fleet consumes IAdapterTenantScope and knows nothing
        // about leases, while the pool client needs the lease half.
        services.TryAddSingleton<AdapterPoolTenantScope>();
        services.TryAddSingleton<IAdapterTenantScope>(p => p.GetRequiredService<AdapterPoolTenantScope>());
        services.TryAddSingleton<IAdapterLeaseScope>(p => p.GetRequiredService<AdapterPoolTenantScope>());

        // The registry the borrower's pipelines are registered in for the length of a lease, and
        // which PipelineRegistryLeaseParticipant empties on release. Registered here rather than left
        // to AdapterBuilder: a pool member is a composition in its own right, and a member that could
        // not resolve the registry would fail at the first lease rather than at startup.
        services.TryAddSingleton<IPipelineRegistryService, PipelineRegistryService>();

        // Nothing to run until the scheduler arrives (increment 7). TryAdd so a host that supplies a
        // real work item keeps it.
        services.TryAddSingleton<IAdapterLeaseWorkItem, NoAdapterLeaseWorkItem>();

        services.AddOptions<AdapterPoolHubClientOptions>()
            .Configure<IOptions<AdapterOptions>, IOptions<AdapterPoolMemberOptions>>(
                (options, adapterOptions, poolOptions) =>
                {
                    options.EndpointUri = adapterOptions.Value.CommunicationControllerServicesUri;
                    options.MemberId = poolOptions.Value.EffectiveMemberId;
                    options.AdapterPoolTenantId = poolOptions.Value.AdapterPoolTenantId;
                    options.AdapterPoolRtId = poolOptions.Value.AdapterPoolRtId;
                    // 🔴 No TenantId. The management connection is tenant-free by construction — see
                    // AdapterPoolHubClient.BuildServiceUri.
                });

        services.TryAddSingleton<AdapterPoolClient>();
        // 🔴 A deferred forwarder, NOT `p => p.GetRequiredService<AdapterPoolClient>()`. That factory
        // closed a cycle — client → hub client → callbacks → client — which Microsoft DI cannot see
        // through, so it never threw and never overflowed; the member simply went silent for ever.
        // See DeferredAdapterPoolHubCallbacks.
        services.TryAddSingleton<IAdapterPoolHubCallbacks, DeferredAdapterPoolHubCallbacks>();
        services.TryAddSingleton<IServiceClientAccessToken, ServiceClientAccessToken>();
        services.TryAddSingleton<IAdapterPoolHubClient, AdapterPoolHubClient>();

        return services;
    }
}
