using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     Implements the polling service that allows to add callbacks that get invoked at a specified interval
/// </summary>
public class PollingService : IPollingService
{
    private readonly ConcurrentDictionary<PollingHandle, PollingItem> _callbacks;
    private readonly ILogger<PollingService> _logger;

    /// <summary>
    ///     Constructor
    /// </summary>
    public PollingService(ILogger<PollingService> logger)
    {
        _logger = logger;
        _callbacks = new();
    }

    /// <inheritdoc />
    public PollingHandle RegisterCallback(TimeSpan interval, Func<Task> callback)
    {
        var handle = new PollingHandle(this);
        var timer = new Timer(TimerCallback, handle, TimeSpan.Zero, interval);
        _callbacks.TryAdd(handle, new PollingItem
        {
            Action = callback,
            Interval = interval,
            Timer = timer,
            LastExecutionTime = DateTime.MinValue
        });

        return handle;
    }

    /// <inheritdoc />
    public void ClearCallbacks()
    {
        _callbacks.Clear();
    }

    /// <inheritdoc />
    public void UnregisterCallback(PollingHandle handle)
    {
        if (_callbacks.TryRemove(handle, out var pollingItem))
        {
            pollingItem.Timer?.Dispose();
        }
    }

    private async void TimerCallback(object? state)
    {
        if (state is not PollingHandle handle)
        {
            return;
        }

        if (_callbacks.TryGetValue(handle, out var pollingItem))
        {
            pollingItem.LastExecutionTime = DateTime.UtcNow;
            try
            {
                await pollingItem.Action();
            }
            catch (PipelineExecutionException)
            {
                // Pipeline may have been unregistered during shutdown/reconfiguration.
                // Swallow the exception to prevent crashing the process from async void context.
            }
            catch (Exception ex)
            {
                // All exceptions must be caught in async void to prevent crashing the process.
                _logger.LogError(ex, "Unhandled exception in polling callback");
            }
        }
    }
}