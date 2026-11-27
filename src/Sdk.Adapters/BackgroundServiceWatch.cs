using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Runs an adapter host and fails the run when one of its background services faulted, or cancelled
///     itself before a shutdown was requested. Once the host has started, the watch, not the host, stops the
///     host on such an end when <see cref="BackgroundServiceExceptionBehavior.StopHost" /> is configured and
///     every hosted service is the instance the host started, so it sees the end before any stop was requested.
/// </summary>
internal sealed class BackgroundServiceWatch
{
    private readonly IReadOnlyList<BackgroundService> _backgroundServices;
    private readonly bool _watchesEveryHostedService;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly HostOptions _hostOptions;
    private readonly ConcurrentQueue<BackgroundService> _servicesCancelledBeforeStop = new();
    private IReadOnlyList<(Task ExecuteTask, Task EndRecording)> _endRecordings = [];
    private bool _stopsHostOnFailure;

    private BackgroundServiceWatch(IReadOnlyList<IHostedService> hostedServices, bool watchesEveryHostedService,
        IHostApplicationLifetime applicationLifetime, HostOptions hostOptions)
    {
        _backgroundServices = hostedServices.OfType<BackgroundService>().ToList();
        _watchesEveryHostedService = watchesEveryHostedService;
        _applicationLifetime = applicationLifetime;
        _hostOptions = hostOptions;
        applicationLifetime.ApplicationStarted.Register(WatchBackgroundServices);
    }

    internal static BackgroundServiceWatch For(IServiceProvider hostServices)
    {
        var hostedServices = hostServices.GetServices<IHostedService>().ToList();
        var hostedServicesResolvedAgain = hostServices.GetServices<IHostedService>();
        var watchesEveryHostedService =
            hostedServices.SequenceEqual(hostedServicesResolvedAgain, ReferenceEqualityComparer.Instance);
        return new BackgroundServiceWatch(hostedServices, watchesEveryHostedService,
            hostServices.GetRequiredService<IHostApplicationLifetime>(),
            hostServices.GetRequiredService<IOptions<HostOptions>>().Value);
    }

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
        var hostStopsOnFailure =
            _hostOptions.BackgroundServiceExceptionBehavior == BackgroundServiceExceptionBehavior.StopHost;
        if (hostStopsOnFailure && _watchesEveryHostedService)
        {
            _hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
            _stopsHostOnFailure = true;
        }

        _endRecordings = _backgroundServices
            .Where(backgroundService => backgroundService.ExecuteTask != null)
            .Select(RecordEndOf)
            .ToList();
    }

    private (Task ExecuteTask, Task EndRecording) RecordEndOf(BackgroundService backgroundService)
    {
        var executeTask = backgroundService.ExecuteTask!;
        var endRecording = executeTask.ContinueWith(endedTask => StopIfEndedBeforeStop(backgroundService, endedTask),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return (executeTask, endRecording);
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
        var recordingsOfEndedServices = _endRecordings
            .Where(endRecording => endRecording.ExecuteTask.IsCompleted)
            .Select(endRecording => endRecording.EndRecording)
            .ToArray();
        Task.WaitAll(recordingsOfEndedServices);

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
}
