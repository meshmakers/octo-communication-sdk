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
    ///     0 = a skip warning may be logged, 1 = already warned for the current fall-behind
    ///     episode. Set on the first coalesced tick and re-armed once the callback catches
    ///     up, so a chronically slow trigger warns once per episode instead of every tick.
    /// </summary>
    public int SkipWarned;
}