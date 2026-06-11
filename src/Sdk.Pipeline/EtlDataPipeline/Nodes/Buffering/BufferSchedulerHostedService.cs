using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;

internal interface IBufferScheduler
{
    void ScheduleOrReplace(Func<Task> action, TimeSpan delay);
    Task StopAsync();
    Task StartAsync(CancellationToken cancellationToken);
}

internal class BufferScheduler(ILogger<BufferScheduler> logger) : IBufferScheduler
{
    private readonly Queue<ExecutionItem> _tasks = [];
    private bool _shouldStop;

    public void ScheduleOrReplace(Func<Task> action, TimeSpan delay)
    {
        logger.LogInformation("Scheduling new task. Delay: {Delay}. Currently {TaskCount} tasks in queue.", delay,
            _tasks.Count);
        _tasks.Enqueue(new ExecutionItem(action, delay));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var t = new Thread(Start);
        t.Start(cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _shouldStop = true;
        return Task.CompletedTask;
    }

    private void Start(object? cancellationToken)
    {
        if (cancellationToken is not CancellationToken token)
        {
            logger.LogWarning("Got not cancellation token -> dont start.");
            return;
        }

        _shouldStop = false;
        while (!_shouldStop && !token.IsCancellationRequested)
        {
            // we only want to run the oldest task and remove it if it's not the last one.
            if (_tasks.TryPeek(out var task) && DateTimeOffset.UtcNow - task!.LastExecution >= task.Delay)
            {
                HandleRetrieveFromBuffer(task);
            }

            Thread.Sleep(1_000);
        }
    }

    private void HandleRetrieveFromBuffer(ExecutionItem task)
    {
        try
        {
            logger.LogInformation("Running buffer retrieval task.");
            task.Action().ConfigureAwait(false).GetAwaiter().GetResult();
            task.LastExecution = DateTimeOffset.UtcNow;

            if (_tasks.Count > 1)
            {
                logger.LogInformation("Ran task the last time. Removing it.");
                _ = _tasks.Dequeue();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run task.");
        }
    }

    private class ExecutionItem(Func<Task> action, TimeSpan delay)
    {
        public Func<Task> Action { get; } = action;
        public TimeSpan Delay { get; } = delay;
        public DateTimeOffset LastExecution { get; set; } = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// </summary>
internal class BufferSchedulerHostedService(IBufferScheduler scheduler) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return scheduler.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return scheduler.StopAsync();
    }
}

internal static class QueueExtensions
{
    public static bool TryPeek<T>(this Queue<T> queue, out T? result)
    {
        if (queue.Count == 0)
        {
            result = default;
            return false;
        }

        result = queue.Peek();
        return true;
    }
}