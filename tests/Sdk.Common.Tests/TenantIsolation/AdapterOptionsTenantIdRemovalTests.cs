using System.Reflection;
using Meshmakers.Octo.Sdk.Common.Adapters;

namespace Sdk.Common.Tests.TenantIsolation;

/// <summary>
///     AB#4924 increment 3, verification mechanism 1 — the compiler guard.
/// </summary>
/// <remarks>
///     <para>
///         The isolation invariant of shared adapter leasing is that a pool member retains nothing
///         tenant-scoped between leases. The single largest threat to it is not a subtle cache: it
///         is a <b>process-wide tenant value that looks like the right answer</b>. While
///         <c>AdapterOptions.TenantId</c> existed, any node or service could read it, and on a
///         leased process it would return the tenant of whoever configured the adapter rather than
///         the tenant of the work item — a cross-tenant read that produces a plausible log line.
///     </para>
///     <para>
///         Deleting the property is the only mechanism that actually works, because it turns every
///         such read into a compile error. This test is what stops it from coming back: a
///         well-meaning change that re-adds <c>TenantId</c> "for convenience" fails here rather than
///         six months later in an incident.
///     </para>
///     <para>
///         Reflection rather than a source-text scan on purpose — it catches a member added by any
///         means (a partial class, a base type, an interface default) and does not depend on the
///         test process being able to find the .cs file on disk.
///     </para>
/// </remarks>
public class AdapterOptionsTenantIdRemovalTests
{
    [Fact]
    public void AdapterOptionsHasNoMemberNamedTenantId()
    {
        const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic
                                                           | BindingFlags.Instance | BindingFlags.Static
                                                           | BindingFlags.FlattenHierarchy;

        var offenders = typeof(AdapterOptions)
            .GetMembers(allMembers)
            .Where(m => m.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase))
            .Select(m => $"{m.MemberType} {m.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "AdapterOptions must not carry a process-wide tenant value (AB#4924). Found: "
            + string.Join(", ", offenders)
            + ". Use AdapterOptions.DedicatedTenantId for connection-level concerns that genuinely "
            + "belong to the process, IEtlContext.TenantId inside a node, or IAdapterTenantScope in "
            + "the services around the node layer.");
    }

    [Fact]
    public void TheReplacementIsNamedSoItCannotBeMistakenForTheExecutionTenant()
    {
        // Not decoration: the rename is half the mitigation. A property called TenantId invites the
        // old assumption straight back, so the guard above is paired with an assertion that the
        // replacement still carries the name that forces a caller to say which tenant it means.
        var replacement = typeof(AdapterOptions).GetProperty(nameof(AdapterOptions.DedicatedTenantId));

        Assert.NotNull(replacement);
        Assert.Equal(typeof(string), replacement!.PropertyType);
    }
}
