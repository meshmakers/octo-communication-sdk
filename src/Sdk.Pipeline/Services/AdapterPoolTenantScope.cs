namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The pool-member implementation of <see cref="IAdapterTenantScope" /> (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     <para>
///         Replaces <see cref="AdapterTenantScope" /> in a process that is a pool member. The
///         difference is the lease: a dedicated adapter only ever has the per-execution tenant, a pool
///         member has a lease tenant that the executions of that lease run inside.
///     </para>
///     <para>
///         🔴 <b>The guard in <see cref="BeginExecution" /> is the part worth reading.</b> While a
///         lease is held, entering an execution for a <i>different</i> tenant throws. Nothing in the
///         SDK is supposed to do that — but "nothing is supposed to" is exactly the assumption that
///         produces a cross-tenant read when some future trigger fires late, or a queued work item is
///         picked up a moment after the lease it belonged to was released. The check turns that into a
///         loud failure on the member instead of a plausible-looking wrong answer in a borrower's
///         data.
///     </para>
/// </remarks>
public sealed class AdapterPoolTenantScope : IAdapterLeaseScope
{
    // Per-execution tenant, exactly as on a dedicated adapter: an AsyncLocal flows into the
    // continuations of the execution that set it and into nothing else. An INSTANCE field, not
    // static, for the same reason AdapterTenantScope gives.
    private readonly AsyncLocal<string?> _executionTenantId = new();

    // 🔴 The lease tenant is process-wide ON PURPOSE and is the one exception to "no process-wide
    // tenant value". It has to be: the lease arrives on a hub callback and the executions it serves
    // run on different async chains, so an AsyncLocal set by the callback would not reach them. What
    // makes it safe is that it is null between leases - the process is tenant-free whenever it is not
    // actively serving one borrower.
    private readonly Lock _leaseLock = new();
    private string? _leaseTenantId;

    /// <inheritdoc />
    public bool IsPoolMember => true;

    /// <inheritdoc />
    public bool HasLease
    {
        get
        {
            lock (_leaseLock)
            {
                return _leaseTenantId is not null;
            }
        }
    }

    /// <inheritdoc />
    public string? LeaseTenantId
    {
        get
        {
            lock (_leaseLock)
            {
                return _leaseTenantId;
            }
        }
    }

    /// <inheritdoc />
    public bool HasTenant => Current is not null;

    /// <inheritdoc />
    public string TenantId =>
        Current ?? throw new InvalidOperationException(
            "No tenant is in scope. This process is an adapter pool member: outside a lease it "
            + "serves no tenant at all, which is the whole isolation invariant of shared adapter "
            + "leasing (AB#4924). Reading a tenant here is a bug — there is no correct answer, and "
            + "a plausible-looking wrong one would be another tenant's data.");

    /// <inheritdoc />
    public IDisposable BeginLease(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        lock (_leaseLock)
        {
            if (_leaseTenantId is not null)
            {
                throw new InvalidOperationException(
                    $"This pool member already holds a lease for tenant '{_leaseTenantId}' and cannot take "
                    + $"a second one for '{tenantId}'. A member serves exactly one tenant at any instant.");
            }

            _leaseTenantId = tenantId;
        }

        return new LeaseRelease(this, tenantId);
    }

    /// <inheritdoc />
    public IDisposable BeginExecution(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var leaseTenantId = LeaseTenantId;
        if (leaseTenantId is not null &&
            !string.Equals(leaseTenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"An execution for tenant '{tenantId}' was started on a pool member that currently holds a "
                + $"lease for tenant '{leaseTenantId}'. A member serves exactly one tenant at any instant; "
                + "this is a cross-tenant execution and is refused.");
        }

        var previous = _executionTenantId.Value;
        _executionTenantId.Value = tenantId;
        return new RestoreExecution(this, previous);
    }

    private string? Current
    {
        get
        {
            var executionTenantId = _executionTenantId.Value;
            if (!string.IsNullOrWhiteSpace(executionTenantId))
            {
                return executionTenantId;
            }

            var leaseTenantId = LeaseTenantId;
            return string.IsNullOrWhiteSpace(leaseTenantId) ? null : leaseTenantId;
        }
    }

    /// <summary>
    ///     Leaves the lease, making the process tenant-free again.
    /// </summary>
    /// <remarks>
    ///     Idempotent, and it refuses to clear a lease other than its own: a stale dispose — a
    ///     release object held past the lease it belonged to — must not strip the tenant off the lease
    ///     that is running now.
    /// </remarks>
    private sealed class LeaseRelease(AdapterPoolTenantScope owner, string tenantId) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (owner._leaseLock)
            {
                if (string.Equals(owner._leaseTenantId, tenantId, StringComparison.Ordinal))
                {
                    owner._leaseTenantId = null;
                }
            }
        }
    }

    /// <summary>
    ///     Restores the enclosing execution's tenant, exactly as on a dedicated adapter — a node that
    ///     starts a sub-pipeline must not strip the tenant off the rest of the outer pipeline.
    /// </summary>
    private sealed class RestoreExecution(AdapterPoolTenantScope owner, string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._executionTenantId.Value = previous;
        }
    }
}
