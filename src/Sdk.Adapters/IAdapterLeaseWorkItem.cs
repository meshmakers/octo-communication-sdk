using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     What the member actually does while it holds a lease (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Deliberately a seam, not an implementation.</b> The SDK cannot know what a member's
///         work is: a mesh adapter runs a pipeline through its orchestrator, another adapter repo may
///         run something else entirely. The adapter repo registers the implementation; the default
///         below does nothing and says so, which is an honest "nothing to run" rather than a silently
///         swallowed work item.
///     </para>
///     <para>
///         🔴 <b>What the lease carries is what the work item runs</b> (AB#4924 §9.9 / D4).
///         <c>LeaseDto</c> names the pipeline, the input and the <b>already existing</b> execution
///         entity the controller created at enqueue and moved to <c>Running</c> when it claimed it. An
///         implementation must execute <i>against</i> that execution id and must not report an
///         execution start for it — the start-report path creates a second entity, and two entities for
///         one piece of work means two billing spans and a queue history that no longer joins up.
///     </para>
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
/// <param name="OutputData">
///     What the pipeline declared as its result (<c>SetPipelineExecutionResult@1</c>), or null.
///     Carried back on the release and written onto the borrower's execution.
/// </param>
/// <remarks>
///     🔴 <b>The output travels on the release because there is no other route</b> (AB#4924 §9.9 / D4).
///     A dedicated adapter reports it on <c>IAdapterHub.ReportExecutionEndAsync</c>, whose tenant and
///     adapter come from the <i>connection</i>; a pool member holds a tenant-free management channel
///     and has no such connection. Without this the borrower's leased execution would complete with an
///     empty <c>OutputData</c> where its own adapter would have filled it.
/// </remarks>
public readonly record struct LeaseWorkOutcome(bool Success, string? StatusMessage, string? OutputData = null)
{
    /// <summary>The work item ran and succeeded.</summary>
    public static LeaseWorkOutcome Succeeded(string? statusMessage = null, string? outputData = null) =>
        new(true, statusMessage, outputData);

    /// <summary>The work item ran and failed.</summary>
    public static LeaseWorkOutcome Failed(string statusMessage) => new(false, statusMessage);
}

/// <summary>
///     The default work item: none (AB#4924).
/// </summary>
/// <remarks>
///     A member composed without a work item implementation takes a lease, finds nothing it knows how
///     to run and hands the lease straight back. That full cycle — enter, nothing, leave, release —
///     is what makes the wire contract verifiable on its own, and it stays the right answer for a
///     member whose adapter repo has no leased execution path.
/// </remarks>
public sealed class NoAdapterLeaseWorkItem : IAdapterLeaseWorkItem
{
    /// <inheritdoc />
    public Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken)
    {
        return Task.FromResult(LeaseWorkOutcome.Succeeded(
            "This pool member has no work item implementation registered, so the lease was taken and "
            + "handed straight back."));
    }
}
