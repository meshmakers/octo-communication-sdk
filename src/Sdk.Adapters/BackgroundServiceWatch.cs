using Microsoft.Extensions.Hosting;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

internal sealed class BackgroundServiceWatch
{
    private readonly IReadOnlyList<BackgroundService> _backgroundServices;
    private BackgroundService? _cancelledBeforeStop;

    internal BackgroundServiceWatch(IEnumerable<IHostedService> hostedServices, CancellationToken applicationStopping)
    {
        _backgroundServices = hostedServices.OfType<BackgroundService>().ToList();
        applicationStopping.Register(RecordServiceCancelledBeforeStop);
    }

    internal void ThrowIfAnyFailed()
    {
        var faultedExecution = _backgroundServices
            .Select(backgroundService => backgroundService.ExecuteTask)
            .FirstOrDefault(executeTask => executeTask is { IsFaulted: true });

        var backgroundFailure = faultedExecution?.Exception;
        if (backgroundFailure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(backgroundFailure.InnerException ?? backgroundFailure).Throw();
        }

        var cancelledService = Volatile.Read(ref _cancelledBeforeStop);
        if (cancelledService != null)
        {
            throw new InvalidOperationException(
                $"Background service {cancelledService.GetType().FullName} was cancelled before a shutdown was requested");
        }
    }

    private void RecordServiceCancelledBeforeStop()
    {
        var cancelledService = _backgroundServices
            .FirstOrDefault(backgroundService => backgroundService.ExecuteTask is { IsCanceled: true });
        Volatile.Write(ref _cancelledBeforeStop, cancelledService);
    }
}
