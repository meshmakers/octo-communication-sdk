using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.Services;

public class PollingServiceTests
{
    [Fact]
    public async Task RegisterCallback_WhenActionSlowerThanInterval_CoalescesConcurrentTicks()
    {
        var service = new PollingService(A.Fake<ILogger<PollingService>>());

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
    }

    [Fact]
    public async Task RegisterCallback_WhenActionFasterThanInterval_RunsEveryTick()
    {
        var service = new PollingService(A.Fake<ILogger<PollingService>>());

        var runs = 0;
        using var handle = service.RegisterCallback(TimeSpan.FromMilliseconds(30), () =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        });

        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Fast callbacks are never coalesced away — the gate only skips overlapping runs.
        Assert.True(runs >= 3, $"expected repeated ticks, got {runs}");
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
}
