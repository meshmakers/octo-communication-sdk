using Meshmakers.Octo.Sdk.Common.Services;

namespace Sdk.Common.Tests.TenantIsolation;

/// <summary>
///     AB#4924 increment 6 — the pool member's tenant scope.
/// </summary>
/// <remarks>
///     🔴 The property under test is concept §4's isolation invariant expressed as code: <b>between
///     two leases the process retains nothing tenant-scoped</b>, and while a lease is held it serves
///     exactly one tenant. Every assertion below is one sentence of that invariant; a regression in
///     any of them is a cross-tenant data incident, not a bug.
/// </remarks>
public class AdapterPoolTenantScopeTests
{
    private static AdapterPoolTenantScope CreateScope() => new();

    [Fact]
    public void ItDeclaresItselfAPoolMember()
    {
        // Not cosmetic: it is how a service decides whether a process-wide tenant exists at all.
        Assert.True(CreateScope().IsPoolMember);
    }

    /// <summary>
    ///     🔴 The invariant's resting state. Between leases there is no tenant, and asking for one
    ///     throws rather than returning a plausible-looking default — which on a shared process would
    ///     be another tenant's data.
    /// </summary>
    [Fact]
    public void BetweenLeasesThereIsNoTenantAtAll()
    {
        var scope = CreateScope();

        Assert.False(scope.HasLease);
        Assert.False(scope.HasTenant);
        Assert.Null(scope.LeaseTenantId);
        Assert.Throws<InvalidOperationException>(() => scope.TenantId);
    }

    [Fact]
    public void ALeaseMakesItsTenantVisibleToTheWholeProcess()
    {
        var scope = CreateScope();

        using var lease = scope.BeginLease("tenant-a");

        Assert.True(scope.HasLease);
        Assert.True(scope.HasTenant);
        Assert.Equal("tenant-a", scope.LeaseTenantId);
        Assert.Equal("tenant-a", scope.TenantId);
    }

    /// <summary>
    ///     🔴 The lease tenant has to be process-wide rather than an <c>AsyncLocal</c>: the lease
    ///     arrives on a hub callback and the executions it serves run on entirely different async call
    ///     chains. A scope that only flowed with the callback would leave every execution tenant-less.
    /// </summary>
    [Fact]
    public async Task TheLeaseTenantIsVisibleOnAnUnrelatedAsyncChain()
    {
        var scope = CreateScope();
        using var lease = scope.BeginLease("tenant-a");

        // Task.Run starts a chain that did not inherit the callback's context in any meaningful way;
        // an AsyncLocal set before it would flow, which is why the assertion below is run through a
        // completely separate entry point started later.
        var observed = await Task.Run(async () =>
        {
            await Task.Yield();
            return scope.TenantId;
        });

        Assert.Equal("tenant-a", observed);
    }

    /// <summary>
    ///     🔴 Releasing the lease makes the process tenant-free again — the other half of "a property
    ///     of time". A release that left the tenant behind is exactly the defect that turns the next
    ///     lease into a cross-tenant read.
    /// </summary>
    [Fact]
    public void ReleasingTheLeaseLeavesNoTenantBehind()
    {
        var scope = CreateScope();
        var lease = scope.BeginLease("tenant-a");

        lease.Dispose();

        Assert.False(scope.HasLease);
        Assert.False(scope.HasTenant);
        Assert.Null(scope.LeaseTenantId);
        Assert.Throws<InvalidOperationException>(() => scope.TenantId);
    }

    [Fact]
    public void ASecondLeaseIsRefusedWhileOneIsHeld()
    {
        var scope = CreateScope();
        using var lease = scope.BeginLease("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => scope.BeginLease("tenant-b"));

        Assert.Contains("tenant-a", exception.Message, StringComparison.Ordinal);
        // And the first lease is untouched — a refused second lease must not disturb the one running.
        Assert.Equal("tenant-a", scope.TenantId);
    }

    [Fact]
    public void ConsecutiveLeasesAreFine()
    {
        var scope = CreateScope();

        using (scope.BeginLease("tenant-a"))
        {
            Assert.Equal("tenant-a", scope.TenantId);
        }

        using (scope.BeginLease("tenant-b"))
        {
            Assert.Equal("tenant-b", scope.TenantId);
        }
    }

    /// <summary>
    ///     🔴 The guard nothing in the SDK is supposed to need. An execution for a tenant other than
    ///     the leased one is refused — because "nothing is supposed to" is exactly the assumption that
    ///     produces a cross-tenant read when a trigger fires late or a queued item is picked up a
    ///     moment after its lease ended.
    /// </summary>
    [Fact]
    public void AnExecutionForAnotherTenantIsRefusedWhileALeaseIsHeld()
    {
        var scope = CreateScope();
        using var lease = scope.BeginLease("tenant-a");

        var exception = Assert.Throws<InvalidOperationException>(() => scope.BeginExecution("tenant-b"));

        Assert.Contains("cross-tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnExecutionForTheLeasedTenantIsAllowed()
    {
        var scope = CreateScope();
        using var lease = scope.BeginLease("tenant-a");

        using var execution = scope.BeginExecution("tenant-a");

        Assert.Equal("tenant-a", scope.TenantId);
    }

    [Fact]
    public void TheTenantComparisonIsCaseInsensitive()
    {
        var scope = CreateScope();
        using var lease = scope.BeginLease("Tenant-A");

        using var execution = scope.BeginExecution("tenant-a");

        Assert.True(scope.HasTenant);
    }

    /// <summary>
    ///     A node that starts a sub-pipeline must not strip the tenant off the rest of the outer one —
    ///     the same nesting contract the dedicated scope makes.
    /// </summary>
    [Fact]
    public void ASubPipelineRestoresTheOuterExecutionRatherThanClearingIt()
    {
        var scope = CreateScope();
        using var lease = scope.BeginLease("tenant-a");
        using var outer = scope.BeginExecution("tenant-a");

        using (scope.BeginExecution("tenant-a"))
        {
            Assert.Equal("tenant-a", scope.TenantId);
        }

        Assert.Equal("tenant-a", scope.TenantId);
    }

    /// <summary>
    ///     🔴 A stale release — one held past the lease it belonged to — must not strip the tenant off
    ///     the lease that is running now. Without this the invariant would fail in the one shape
    ///     nobody writes a test for: correct code, wrong lifetime.
    /// </summary>
    [Fact]
    public void AStaleLeaseReleaseDoesNotClearTheCurrentLease()
    {
        var scope = CreateScope();
        var first = scope.BeginLease("tenant-a");
        first.Dispose();

        using var second = scope.BeginLease("tenant-b");
        first.Dispose();

        Assert.True(scope.HasLease);
        Assert.Equal("tenant-b", scope.TenantId);
    }

    [Fact]
    public void AnExecutionWithoutALeaseStillCarriesItsOwnTenant()
    {
        // A pool member is not the only consumer of this type in a test, and a dedicated execution
        // path must keep working: the execution tenant stands on its own when no lease is held.
        var scope = CreateScope();

        using (scope.BeginExecution("tenant-a"))
        {
            Assert.Equal("tenant-a", scope.TenantId);
        }

        Assert.False(scope.HasTenant);
    }
}
