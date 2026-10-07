using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Stops a pool member's process once it is draining and idle, so the pool workload restarts it
///     as a fresh, clean process (AB#5864).
/// </summary>
/// <remarks>
///     <para>
///         The drain contract always said "it finishes what it holds, takes no further lease and
///         <b>exits</b>" (<c>ILeaseService.DrainMemberAsync</c> in the controller, concept §6) — and no
///         member ever exited. A drained member sat <c>1/1 Running</c> for the rest of its life: on
///         test-2-dev a lease participant that failed to leave drained the only member of a pool,
///         and every later execution of every borrower failed until somebody deleted the pod.
///     </para>
///     <para>
///         Exiting is the recovery, not a punishment. A member drains because its cleanliness is
///         unproven (a participant failed to leave, its lease expired server-side) or because the
///         pool is giving it up; in neither case can the process be made trustworthy again from the
///         inside. A container restart can: the new process starts with no tenant state at all,
///         registers as available, and the controller's registry drops the old registration as
///         superseded (same member id, new connection).
///     </para>
///     <para>
///         🔴 It waits for <see cref="AdapterPoolClient.IsDrainedAndIdle" />, never only for the drain
///         flag. A drain that arrives during a lease lets the lease finish; stopping earlier would
///         lose the release report, and the controller would then interrupt and re-run work that
///         had already completed.
///     </para>
///     <para>
///         Stopping the host is a graceful shutdown — the management connection is closed cleanly by
///         <see cref="AdapterPoolMemberService.StopAsync" />, which the controller sees as a
///         disconnect of a member that holds no lease, so nothing is re-queued. Disabled with
///         <c>AdapterPool:ExitWhenDrained=false</c> (<see cref="AdapterPoolMemberOptions.ExitWhenDrained" />);
///         the member then only drops out of readiness and stays until something else restarts it.
///     </para>
/// </remarks>
public sealed class AdapterPoolMemberDrainExitService : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly AdapterLifetimeManagement _lifetimeManagement;
    private readonly IOptions<AdapterPoolMemberOptions> _options;
    private readonly AdapterPoolClient _poolClient;

    /// <summary>Constructor.</summary>
    /// <param name="poolClient">The member's half of the lease protocol, which knows whether it drains.</param>
    /// <param name="options">The pool member configuration (<see cref="AdapterPoolMemberOptions.ExitWhenDrained" />).</param>
    /// <param name="lifetimeManagement">Stops the host.</param>
    public AdapterPoolMemberDrainExitService(AdapterPoolClient poolClient,
        IOptions<AdapterPoolMemberOptions> options, AdapterLifetimeManagement lifetimeManagement)
    {
        _poolClient = poolClient;
        _options = options;
        _lifetimeManagement = lifetimeManagement;
    }

    /// <summary>How often the drain state is sampled. Internal for the tests.</summary>
    internal TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.ExitWhenDrained)
        {
            Logger.Info(
                "Exit-when-drained is disabled by configuration (AdapterPool:ExitWhenDrained); a draining member " +
                "only drops out of readiness");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (Evaluate())
            {
                return;
            }
        }
    }

    /// <summary>
    ///     One sample: stops the host when the member is drained and idle and reports whether it did.
    ///     Internal so the decision can be tested without the timer.
    /// </summary>
    internal bool Evaluate()
    {
        if (!_poolClient.IsDrainedAndIdle)
        {
            return false;
        }

        // Error, not Info: this is the one line that explains why a pool member restarted, and it
        // has to be findable among the WARN-level audit noise of the leases before it.
        Logger.Error(
            "This pool member is draining ({Reason}) and holds no lease. Stopping the process so the pool " +
            "workload restarts it as a fresh member",
            _poolClient.DrainReason ?? "no reason recorded");
        _lifetimeManagement.Stop();
        return true;
    }
}
