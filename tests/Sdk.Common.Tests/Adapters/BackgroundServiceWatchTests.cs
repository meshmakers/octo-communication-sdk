using Meshmakers.Octo.Sdk.Common.Adapters;
using Microsoft.Extensions.Hosting;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     A background service that ends on its own stops the host as if it was asked to; the watch tells
///     such an end apart from the cancellation a requested shutdown causes.
/// </summary>
public sealed class BackgroundServiceWatchTests
{
    private readonly CancellationTokenSource _applicationStopping = new();

    private static async Task StartAndWaitForExecutionToEnd(BackgroundService backgroundService)
    {
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.WhenAny(backgroundService.ExecuteTask!);
    }

    [Fact]
    public async Task AFaultedBackgroundServiceIsRaisedWithItsOwnException()
    {
        var faultedService = new BackgroundServiceEndingWith(
            Task.FromException(new InvalidOperationException("background work failed")));
        var watch = new BackgroundServiceWatch([faultedService], _applicationStopping.Token);
        await StartAndWaitForExecutionToEnd(faultedService);
        await _applicationStopping.CancelAsync();

        var raisedException = Assert.Throws<InvalidOperationException>(watch.ThrowIfAnyFailed);

        Assert.Equal("background work failed", raisedException.Message);
    }

    [Fact]
    public async Task ABackgroundServiceCancelledBeforeTheShutdownRequestIsAFailure()
    {
        var cancelledService = new BackgroundServiceEndingWith(Task.FromCanceled(new CancellationToken(true)));
        var watch = new BackgroundServiceWatch([cancelledService], _applicationStopping.Token);
        await StartAndWaitForExecutionToEnd(cancelledService);
        await _applicationStopping.CancelAsync();

        var raisedException = Assert.Throws<InvalidOperationException>(watch.ThrowIfAnyFailed);

        Assert.Contains(nameof(BackgroundServiceEndingWith), raisedException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABackgroundServiceCancelledByTheShutdownIsNoFailure()
    {
        var executionEnd = new TaskCompletionSource();
        var serviceCancelledByShutdown = new BackgroundServiceEndingWith(executionEnd.Task);
        var watch = new BackgroundServiceWatch([serviceCancelledByShutdown], _applicationStopping.Token);
        await serviceCancelledByShutdown.StartAsync(CancellationToken.None);

        await _applicationStopping.CancelAsync();
        executionEnd.SetCanceled(_applicationStopping.Token);
        await Task.WhenAny(serviceCancelledByShutdown.ExecuteTask!);

        watch.ThrowIfAnyFailed();
    }

    [Fact]
    public async Task CompletedNotStartedAndPlainHostedServicesAreNoFailure()
    {
        var completedService = new BackgroundServiceEndingWith(Task.CompletedTask);
        var notStartedService = new BackgroundServiceEndingWith(Task.CompletedTask);
        var watch = new BackgroundServiceWatch(
            [completedService, notStartedService, new PlainHostedService()], _applicationStopping.Token);
        await StartAndWaitForExecutionToEnd(completedService);
        await _applicationStopping.CancelAsync();

        watch.ThrowIfAnyFailed();
    }

    private sealed class BackgroundServiceEndingWith(Task outcome) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => outcome;
    }

    private sealed class PlainHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
