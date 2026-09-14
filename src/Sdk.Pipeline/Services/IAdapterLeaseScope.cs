namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The lease half of <see cref="IAdapterTenantScope" />, implemented only by a pool member
///     (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Two nested notions of "the current tenant", and keeping them apart is the point.</b>
///         A <b>lease</b> binds the whole process to one borrowing tenant for the duration of one work
///         item; an <b>execution</b> is one pipeline run inside it. On a dedicated adapter only the
///         execution notion exists, which is why <see cref="IAdapterTenantScope" /> is the interface
///         everything else in the fleet consumes and this one adds nothing to it for them.
///     </para>
///     <para>
///         The distinction is load-bearing rather than cosmetic. The lease arrives on a hub callback
///         and the executions it serves run on entirely different async call chains, so an
///         <c>AsyncLocal</c> set by the callback would not flow into them — the lease tenant has to be
///         a value the whole process can see. That is exactly the process-wide tenant value concept §4
///         warns about, and it is safe here for one reason only: <b>it exists only while a lease is
///         held</b>. Between leases there is no tenant at all and reading one throws. The isolation
///         invariant is a property of time, as the concept says, and this is where that sentence
///         becomes code.
///     </para>
/// </remarks>
public interface IAdapterLeaseScope : IAdapterTenantScope
{
    /// <summary>Whether a lease is currently held.</summary>
    bool HasLease { get; }

    /// <summary>
    ///     The borrowing tenant of the current lease, or <c>null</c> between leases.
    /// </summary>
    /// <remarks>
    ///     Deliberately nullable while <see cref="IAdapterTenantScope.TenantId" /> throws: a caller
    ///     asking "is a lease held and for whom" has a legitimate no-lease answer, whereas a caller
    ///     reaching for the tenant of the work it is doing outside any work has a bug.
    /// </remarks>
    string? LeaseTenantId { get; }

    /// <summary>
    ///     Enters a lease. Dispose to leave it — which is what makes the process tenant-free again.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     A lease is already held. 🔴 Never a silent overwrite: two overlapping leases on one process
    ///     is precisely the cross-tenant data incident the design exists to make impossible, so it
    ///     fails loudly at the moment the second one arrives rather than producing an execution that
    ///     reads the wrong tenant.
    /// </exception>
    IDisposable BeginLease(string tenantId);
}
