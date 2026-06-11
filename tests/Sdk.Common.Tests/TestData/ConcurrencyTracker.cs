namespace Sdk.Common.Tests.TestData;

/// <summary>
/// Thread-safe tracker for measuring the maximum number of concurrent executions.
/// </summary>
internal class ConcurrencyTracker
{
    private int _current;
    private int _max;

    /// <summary>
    /// Gets the maximum number of concurrent executions observed.
    /// </summary>
    public int MaxConcurrent => Volatile.Read(ref _max);

    /// <summary>
    /// Gets the total number of completed executions.
    /// </summary>
    public int TotalExecutions => _totalExecutions;

    private int _totalExecutions;

    public void Enter()
    {
        var current = Interlocked.Increment(ref _current);
        // Update max using compare-and-swap
        int max;
        do
        {
            max = Volatile.Read(ref _max);
        } while (current > max && Interlocked.CompareExchange(ref _max, current, max) != max);
    }

    public void Exit()
    {
        Interlocked.Decrement(ref _current);
        Interlocked.Increment(ref _totalExecutions);
    }
}
