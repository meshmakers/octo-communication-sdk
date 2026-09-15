using System.Reflection;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Sdk.Common.Tests.TenantIsolation;

/// <summary>
///     AB#4924 increment 3, verification mechanism 2 — the DI sweep.
/// </summary>
/// <remarks>
///     <para>
///         This is the test meant to catch the regression <b>nobody is thinking about</b> six months
///         from now. A singleton that caches something tenant-derived is invisible on a dedicated
///         adapter — it holds one tenant's data for the life of a process that only ever serves one
///         tenant — and becomes a cross-tenant read the moment the same process is leased to a
///         second tenant.
///     </para>
///     <para>
///         So the rule is inverted: a singleton is guilty until somebody has looked at it. Any type
///         registered as a singleton in the adapter host whose surface mentions a tenant must be on
///         the allow-list below with a reason, or this fails. Adding a new singleton that touches a
///         tenant therefore fails the build until a human has decided it is safe.
///     </para>
///     <para>
///         ⚠️ Scope of what this can see: it sweeps the service <b>descriptors</b> the SDK registers,
///         which is where the SDK's own singletons are. Domain services registered by an adapter
///         repository (<c>octo-mesh-adapter</c> and friends) are not visible from here — the same
///         sweep has to exist on their side, over their own registration extension.
///     </para>
/// </remarks>
public class SingletonTenantFreedomSweepTests
{
    /// <summary>
    ///     Singleton types cleared as tenant-free, each with the reason it is safe.
    /// </summary>
    /// <remarks>
    ///     Keyed by type name rather than <see cref="Type" /> so the list stays readable and does not
    ///     force the test project to reference every assembly a registration comes from.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> ClearedSingletons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AdapterTenantScope"] =
                "Holds the per-execution tenant in an AsyncLocal, which flows with the execution and "
                + "is restored when it ends. The instance holds no tenant between executions - that "
                + "is asserted directly by AdapterTenantScopeInterleaveTests.",
            ["ConfigureLegacyAdapterTenantId"] =
                "Reads the deprecated Adapter:TenantId configuration key once, at options "
                + "construction, and writes it onto AdapterOptions.DedicatedTenantId. Configuration "
                + "is process-level by definition and this is the dedicated tenant, not an "
                + "execution's.",
            ["ConfigureAdapterAuthenticatorOptions"] =
                "Projects the adapter's OWN credential (AB#5072). The tenant it carries is the "
                + "dedicated tenant for the adapter's own hub connection; a pool member has none and "
                + "acquires the borrower's credential per lease instead.",
            ["AdapterPoolTenantScope"] =
                "AB#4924 increment 6. Holds the LEASE tenant in a process-wide field, which is the "
                + "one deliberate exception to 'no process-wide tenant' - the lease arrives on a hub "
                + "callback and the executions it serves run on other async chains, so an AsyncLocal "
                + "would not reach them. What makes it safe is that the field is null between leases, "
                + "which AdapterPoolTenantScopeTests asserts directly, and that an execution for a "
                + "tenant other than the leased one is refused.",
            ["AdapterPoolClient"] =
                "AB#4924 increment 6. Drives one lease at a time and holds no tenant of its own: the "
                + "lease it is working on lives on IAdapterLeaseScope, and the LeaseDto is a parameter "
                + "rather than a field. AdapterPoolClientTests pins that the tenant is gone from the "
                + "process before the release is reported.",
        };

    /// <summary>
    ///     Singleton registrations made through a factory, cleared by SERVICE type with a reason.
    /// </summary>
    /// <remarks>
    ///     🔴 A factory hides the implementation type, which is exactly what this sweep exists to
    ///     inspect — so a factory registration is an offender unless somebody named it here. The two
    ///     entries below are aliases onto an instance that is itself on
    ///     <see cref="ClearedSingletons" />, which is the only shape that deserves the exemption.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> ClearedFactorySingletons =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IAdapterTenantScope"] =
                "Alias onto the single AdapterPoolTenantScope instance, which is cleared above.",
            ["IAdapterLeaseScope"] =
                "Alias onto the single AdapterPoolTenantScope instance, which is cleared above.",
            ["IAdapterPoolHubCallbacks"] =
                "DeferredAdapterPoolHubCallbacks: holds an IServiceProvider and nothing else, and "
                + "resolves AdapterPoolClient per call. It exists to break the construction cycle "
                + "client -> hub client -> callbacks -> client, which Microsoft DI could not see "
                + "through a factory lambda and which hung the member process silently. It carries no "
                + "tenant state of its own; the instance it forwards to is cleared above.",
        };

    /// <summary>
    ///     Members whose name mentions a tenant but which are demonstrably not a cached tenant.
    /// </summary>
    private static readonly IReadOnlySet<string> IgnoredMemberNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // A method parameter or a per-call argument is not retained state.
            "BeginExecution",
        };

    [Fact]
    public void EverySingletonThatMentionsATenantIsOnTheClearedList()
    {
        var services = new ServiceCollection();

        // Register the SDK singletons this increment is responsible for. Deliberately explicit
        // rather than driving the whole AdapterBuilder: that would pull a message bus, NLog and a
        // hosted-service graph into a unit test, and the failure mode would be an unrelated
        // startup error rather than a readable assertion.
        services.AddSingleton<IAdapterTenantScope, AdapterTenantScope>();

        var offenders = SweepFor(services);

        Assert.True(offenders.Count == 0,
            "A singleton in the adapter host touches a tenant without having been cleared "
            + "(AB#4924). Decide whether it retains tenant state across executions; if it does not, "
            + "add it to ClearedSingletons with the reason. Offenders: "
            + string.Join("; ", offenders));
    }

    /// <summary>
    ///     AB#4924 increment 6 — the same sweep over the <b>pool member</b> composition.
    /// </summary>
    /// <remarks>
    ///     🔴 A pool member is where a retained tenant actually costs something: on a dedicated adapter
    ///     a singleton holding one tenant's data is invisible, because the process only ever serves
    ///     that tenant. This is the composition where it becomes a cross-tenant read, so it gets its
    ///     own sweep rather than riding on the dedicated one.
    /// </remarks>
    [Fact]
    public void EverySingletonOfThePoolMemberCompositionIsOnTheClearedList()
    {
        var services = new ServiceCollection();
        services.AddAdapterPoolMember();

        var offenders = SweepFor(services);

        Assert.True(offenders.Count == 0,
            "A singleton in the adapter POOL MEMBER host touches a tenant without having been cleared "
            + "(AB#4924). On a pool member a retained tenant is a cross-tenant read, not a harmless "
            + "cache. Offenders: " + string.Join("; ", offenders));
    }

    private static List<string> SweepFor(IServiceCollection services)
    {
        var offenders = new List<string>();

        foreach (var descriptor in services)
        {
            if (descriptor.Lifetime != ServiceLifetime.Singleton)
            {
                continue;
            }

            var implementationType = descriptor.ImplementationType
                                     ?? descriptor.ImplementationInstance?.GetType();
            if (implementationType is null)
            {
                // A factory registration hides its implementation type. That is itself worth
                // knowing about, because it hides exactly what this sweep exists to inspect.
                if (!ClearedFactorySingletons.ContainsKey(descriptor.ServiceType.Name))
                {
                    offenders.Add($"{descriptor.ServiceType.Name}: registered by factory, "
                                  + "implementation type not inspectable");
                }

                continue;
            }

            if (!MentionsATenant(implementationType))
            {
                continue;
            }

            if (!ClearedSingletons.ContainsKey(implementationType.Name))
            {
                offenders.Add($"{implementationType.Name}: singleton whose surface mentions a "
                              + "tenant and which is not on the cleared list");
            }
        }

        return offenders;
    }

    [Fact]
    public void TheClearedListHasNoStaleEntries()
    {
        // An allow-list that keeps entries for types that no longer exist stops being read. Every
        // entry must still name a real type in the SDK.
        var sdkTypes = typeof(AdapterTenantScope).Assembly.GetTypes()
            .Concat(typeof(AdapterOptions).Assembly.GetTypes())
            // The pool-member composition aliases a contract interface (IAdapterPoolHubCallbacks),
            // which lives in Communication.Contracts rather than in either SDK assembly.
            .Concat(typeof(Meshmakers.Octo.Communication.Contracts.Hubs.IAdapterPoolHubCallbacks)
                .Assembly.GetTypes())
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = ClearedSingletons.Keys.Where(name => !sdkTypes.Contains(name)).ToList();
        stale.AddRange(ClearedFactorySingletons.Keys.Where(name => !sdkTypes.Contains(name)));

        Assert.True(stale.Count == 0,
            "ClearedSingletons names types that no longer exist: " + string.Join(", ", stale));
    }

    private static bool MentionsATenant(Type type)
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic
                                                        | BindingFlags.Instance | BindingFlags.Static
                                                        | BindingFlags.DeclaredOnly;

        return type.GetMembers(members)
            .Where(m => !IgnoredMemberNames.Contains(m.Name))
            .Any(m => m.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase))
               || type.GetFields(members)
                   .Any(f => f.FieldType.Name.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
    }
}
