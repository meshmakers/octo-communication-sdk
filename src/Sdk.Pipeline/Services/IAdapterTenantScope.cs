namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The tenant the current pipeline execution is acting for (AB#4924, increment 3).
/// </summary>
/// <remarks>
///     <para>
///         🔴 This exists because <c>AdapterOptions.TenantId</c> was <b>deleted</b>, not deprecated.
///         A process-wide tenant value is the whole hazard of shared adapter leasing: while one
///         exists, any service or node can read it instead of the tenant of the work item it is
///         actually running, and the wrong answer looks entirely plausible in a log. Deleting the
///         property turns every such read into a compile error, and this interface is what the
///         reads become.
///     </para>
///     <para>
///         The value is <b>per execution</b>, not per process, and it is set that way TODAY — on a
///         dedicated adapter it happens to equal the adapter's own tenant on every execution, which
///         is why increment 3 is behaviour-preserving. Nothing about this contract changes when
///         leasing arrives; only who supplies the value does.
///     </para>
///     <para>
///         Prefer <c>IEtlContext.TenantId</c> / <c>INodeContext</c> inside a node — the node layer
///         already carries the tenant explicitly and does not need this. This interface is for the
///         services around the node layer (HTTP routing, token acquisition, caches) that used to
///         reach for the process-wide value.
///     </para>
/// </remarks>
public interface IAdapterTenantScope
{
    /// <summary>
    ///     Whether this process serves more than one tenant over its lifetime, one lease at a time.
    /// </summary>
    /// <remarks>
    ///     Always <c>false</c> in increment 3 and for every dedicated adapter. It is not
    ///     configurable: a process that is not built as a pool member must not be able to become one
    ///     by environment variable. Increment 6 supplies the implementation that can return true.
    /// </remarks>
    bool IsPoolMember { get; }

    /// <summary>
    ///     Whether a tenant is currently in scope. False outside any execution, and on a pool member
    ///     between leases.
    /// </summary>
    bool HasTenant { get; }

    /// <summary>
    ///     The tenant of the current execution.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     No tenant is in scope. Deliberately an exception rather than a null or a fallback to some
    ///     ambient default: a caller that reaches for the tenant outside an execution has a bug, and
    ///     the one thing that must never happen is for it to receive a plausible-looking wrong
    ///     answer.
    /// </exception>
    string TenantId { get; }

    /// <summary>
    ///     Enters the tenant for one execution. Dispose to leave it.
    /// </summary>
    /// <remarks>
    ///     Nested entries are permitted and restore the previous value on dispose — a node that
    ///     starts a sub-pipeline runs inside the outer execution's scope.
    /// </remarks>
    IDisposable BeginExecution(string tenantId);
}
