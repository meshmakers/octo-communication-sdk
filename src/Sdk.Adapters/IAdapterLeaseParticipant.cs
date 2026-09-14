using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     One piece of the process that has to be set up when a lease starts and torn down when it ends
///     (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>This interface is the isolation invariant made composable.</b> Concept §4: "between
///         two leases the process must retain nothing tenant-scoped". The things that are tenant-scoped
///         in a real adapter — the access token, the CK model cache, the hub registration, the tenant
///         repository handle — do not live in one place and do not belong to this SDK. Rather than
///         hard-coding a list the SDK cannot see, each of them registers a participant; the pool
///         client enters them in registration order and leaves them in <b>reverse</b> order, like a
///         stack.
///     </para>
///     <para>
///         The practical consequence is that "did this member really drop everything" becomes a
///         question a test can answer by enumerating participants, rather than a claim somebody has to
///         re-verify by reading code every time a cache is added.
///     </para>
///     <para>
///         <see cref="LeaveLeaseAsync" /> runs on <b>every</b> path — success, failure, drain — and
///         must not throw for a state it never entered. A participant whose leave is skipped because
///         its enter failed is exactly how a tenant survives a release.
///     </para>
/// </remarks>
public interface IAdapterLeaseParticipant
{
    /// <summary>
    ///     Prepares this piece of the process for the borrowing tenant.
    /// </summary>
    /// <remarks>
    ///     🔴 <paramref name="lease" /> carries the borrower's client secret. It is lease-scoped: a
    ///     participant may use it to obtain a token, and must not persist it, write it to
    ///     configuration, or log it.
    /// </remarks>
    Task EnterLeaseAsync(LeaseDto lease, CancellationToken cancellationToken);

    /// <summary>
    ///     Drops everything this piece of the process holds for the borrowing tenant.
    /// </summary>
    Task LeaveLeaseAsync(LeaseDto lease, CancellationToken cancellationToken);
}
