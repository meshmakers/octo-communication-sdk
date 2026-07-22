using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.Services;

public class PollingServiceTests
{
    [Fact]
    public async Task RegisterCallback_WhenActionSlowerThanInterval_CoalescesConcurrentTicks()
    {
        var logger = new CountingLogger<PollingService>();
        var service = new PollingService(logger);

        var concurrent = 0;
        var maxConcurrent = 0;
        var runs = 0;

        using var handle = service.RegisterCallback(TimeSpan.FromMilliseconds(20), async () =>
        {
            Interlocked.Increment(ref runs);
            var current = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, current);
            // Deliberately slower than the 20ms interval so the timer fires again
            // while this callback is still running.
            await Task.Delay(120);
            Interlocked.Decrement(ref concurrent);
        });

        // Window covers several timer periods; without coalescing the slow callback
        // would overlap itself many times over.
        await Task.Delay(400, TestContext.Current.CancellationToken);

        Assert.True(runs >= 2, $"expected the timer to fire multiple times, got {runs}");
        Assert.Equal(1, maxConcurrent);
        // Under-running is surfaced at Warning, but hard-throttled to at most once a minute
        // per trigger: many ticks are skipped in this 400ms window, yet exactly one warns.
        Assert.Equal(1, logger.WarningCount);
    }

    [Fact]
    public async Task RegisterCallback_WhenActionFasterThanInterval_RunsEveryTick()
    {
        var logger = new CountingLogger<PollingService>();
        var service = new PollingService(logger);

        var runs = 0;
        using var handle = service.RegisterCallback(TimeSpan.FromMilliseconds(30), () =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        });

        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Fast callbacks are never coalesced away - the gate only skips overlapping runs.
        Assert.True(runs >= 3, $"expected repeated ticks, got {runs}");
        // ...and with no coalescing there is nothing to warn about.
        Assert.Equal(0, logger.WarningCount);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    /// <summary>Minimal ILogger that counts Warning-level entries, for asserting the skip-warning behavior.</summary>
    private sealed class CountingLogger<T> : ILogger<T>
    {
        private int _warnings;
        public int WarningCount => Volatile.Read(ref _warnings);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warnings);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
