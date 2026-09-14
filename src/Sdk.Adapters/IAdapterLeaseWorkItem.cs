using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     What the member actually does while it holds a lease (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     🔴 <b>Deliberately a seam, not an implementation.</b> Increment 6 ships the lease wire
///     contract: a lease can be granted, routed, entered, left and reported. <i>Which</i> work item a
///     lease serves comes from the queue and the scheduler, which are increment 7. The default
///     implementation therefore does nothing and says so, and the member releases immediately — which
///     is an honest "nothing to run", not a silently swallowed work item.
/// </remarks>
public interface IAdapterLeaseWorkItem
{
    /// <summary>
    ///     Runs the work the lease was granted for. Called inside the lease scope, after every
    ///     participant entered and before any of them leaves.
    /// </summary>
    Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken);
}

/// <summary>
///     Outcome of the work a lease was granted for.
/// </summary>
/// <param name="Success">Whether the work item succeeded.</param>
/// <param name="StatusMessage">
///     Human-readable outcome, carried back to the controller on the release. 🔴 Must never contain
///     credential material — it is logged and stored on the borrower's execution.
/// </param>
public readonly record struct LeaseWorkOutcome(bool Success, string? StatusMessage)
{
    /// <summary>The work item ran and succeeded.</summary>
    public static LeaseWorkOutcome Succeeded(string? statusMessage = null) => new(true, statusMessage);

    /// <summary>The work item ran and failed.</summary>
    public static LeaseWorkOutcome Failed(string statusMessage) => new(false, statusMessage);
}

/// <summary>
///     The default work item: none (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     A member with no scheduler behind it takes a lease, finds nothing queued for it and hands the
///     lease straight back. That full cycle — enter, nothing, leave, release — is precisely what makes
///     the wire contract verifiable one increment before the scheduler exists.
/// </remarks>
public sealed class NoAdapterLeaseWorkItem : IAdapterLeaseWorkItem
{
    /// <inheritdoc />
    public Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken)
    {
        return Task.FromResult(LeaseWorkOutcome.Succeeded(
            "No work item is associated with this lease; the scheduler that supplies one arrives with "
            + "AB#4924 increment 7."));
    }
}
