using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Runs an adapter host and fails the run when one of its background services faulted, or cancelled
///     itself before a shutdown was requested. The watch, not the host, stops the host on such an end when
///     <see cref="BackgroundServiceExceptionBehavior.StopHost" /> is configured, so it sees the end before
///     any stop was requested.
/// </summary>
internal sealed class BackgroundServiceWatch
{
    private readonly IReadOnlyList<BackgroundService> _backgroundServices;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly bool _stopsHostOnFailure;
    private readonly ConcurrentQueue<BackgroundService> _servicesCancelledBeforeStop = new();

    private BackgroundServiceWatch(IEnumerable<IHostedService> hostedServices,
        IHostApplicationLifetime applicationLifetime, bool stopsHostOnFailure)
    {
        _backgroundServices = hostedServices.OfType<BackgroundService>().ToList();
        _applicationLifetime = applicationLifetime;
        _stopsHostOnFailure = stopsHostOnFailure;
        applicationLifetime.ApplicationStarted.Register(WatchBackgroundServices);
    }

    internal static void TakeOverHostStopOnFailure(IServiceCollection services)
    {
        var configuredBehavior = new ConfiguredExceptionBehavior();
        services.AddSingleton(configuredBehavior);
        services.PostConfigure<HostOptions>(hostOptions =>
        {
            configuredBehavior.StopsHost =
                hostOptions.BackgroundServiceExceptionBehavior == BackgroundServiceExceptionBehavior.StopHost;
            hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });
    }

    internal static BackgroundServiceWatch For(IServiceProvider hostServices) =>
        new(hostServices.GetServices<IHostedService>(),
            hostServices.GetRequiredService<IHostApplicationLifetime>(),
            hostServices.GetRequiredService<ConfiguredExceptionBehavior>().StopsHost);

    internal void Run(Action runHost)
    {
        try
        {
            runHost();
        }
        catch (OperationCanceledException)
        {
            ThrowIfAnyFailed();
            throw;
        }

        ThrowIfAnyFailed();
    }

    internal async Task RunAsync(Func<Task> runHost)
    {
        try
        {
            await runHost();
        }
        catch (OperationCanceledException)
        {
            ThrowIfAnyFailed();
            throw;
        }

        ThrowIfAnyFailed();
    }

    private void WatchBackgroundServices()
    {
        foreach (var backgroundService in _backgroundServices)
        {
            backgroundService.ExecuteTask?.ContinueWith(endedTask => StopIfEndedBeforeStop(backgroundService, endedTask),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void StopIfEndedBeforeStop(BackgroundService backgroundService, Task endedTask)
    {
        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return;
        }

        if (endedTask.IsCanceled)
        {
            _servicesCancelledBeforeStop.Enqueue(backgroundService);
        }

        if (_stopsHostOnFailure && (endedTask.IsCanceled || endedTask.IsFaulted))
        {
            _applicationLifetime.StopApplication();
        }
    }

    private void ThrowIfAnyFailed()
    {
        var faultedService = _backgroundServices.FirstOrDefault(backgroundService =>
            backgroundService.ExecuteTask is { IsFaulted: true });
        if (faultedService != null)
        {
            var fault = faultedService.ExecuteTask!.Exception!.InnerException!;
            throw new InvalidOperationException(
                $"Background service {faultedService.GetType().FullName} failed: {fault.GetType().FullName}: {fault.Message}",
                fault);
        }

        if (_servicesCancelledBeforeStop.TryPeek(out var cancelledService))
        {
            var cancellation = CancellationOf(cancelledService.ExecuteTask!);
            throw new InvalidOperationException(
                $"Background service {cancelledService.GetType().FullName} was cancelled before a shutdown was requested: {cancellation.GetType().FullName}: {cancellation.Message}",
                cancellation);
        }
    }

    private static OperationCanceledException CancellationOf(Task cancelledTask)
    {
        try
        {
            cancelledTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException cancellation)
        {
            return cancellation;
        }

        throw new UnreachableException();
    }

    private sealed class ConfiguredExceptionBehavior
    {
        internal bool StopsHost { get; set; } = true;
    }
}
