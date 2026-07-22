namespace Meshmakers.Octo.Sdk.Common.Services;

internal record PollingItem
{
    public required Func<Task> Action { get; init; }
    public required TimeSpan Interval { get; init; }
    public required DateTime LastExecutionTime { get; set; }

    public required Timer? Timer { get; init; }

    /// <summary>
    ///     0 = idle, 1 = the callback is currently running. Guarded via Interlocked so a
    ///     timer tick that fires while the previous callback is still in flight coalesces
    ///     instead of stacking a second concurrent invocation.
    /// </summary>
    public int IsExecuting;

    /// <summary>
    ///     <see cref="DateTime.Ticks"/> (UTC) of the last logged skip warning, 0 if never.
    ///     Accessed via Interlocked so a chronically under-running trigger warns at most once
    ///     per throttle window instead of on every coalesced tick.
    /// </summary>
    public long LastSkipWarnedTicks;
}