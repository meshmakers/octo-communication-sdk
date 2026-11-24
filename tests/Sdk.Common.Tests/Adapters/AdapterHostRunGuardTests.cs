using Meshmakers.Octo.Sdk.Common.Adapters;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     An adapter whose host fails to build or start must end its process with a non-zero exit code
///     and one error line naming the reason; a host that stops on request must still end with 0.
/// </summary>
public sealed class AdapterHostRunGuardTests
{
    private readonly MemoryTarget _memoryTarget;
    private readonly Logger _logger;

    public AdapterHostRunGuardTests()
    {
        var logFactory = new LogFactory();
        var loggingConfiguration = new LoggingConfiguration(logFactory);
        _memoryTarget = new MemoryTarget("memory") { Layout = "${level:uppercase=true}|${message}" };
        loggingConfiguration.AddRuleForAllLevels(_memoryTarget);
        logFactory.Configuration = loggingConfiguration;
        _logger = logFactory.GetLogger(nameof(AdapterHostRunGuardTests));
    }

    private IReadOnlyList<string> ErrorLines =>
        _memoryTarget.Logs.Where(line => line.StartsWith("ERROR|", StringComparison.Ordinal)).ToList();

    private static bool NoStopRequested() => false;

    private static bool StopRequested() => true;

    [Fact]
    public void AHostThatThrowsEndsWithANonZeroExitCode()
    {
        var exitCode = AdapterHostRunGuard.Run(
            () => throw new InvalidOperationException("datastore host mongo-0 does not resolve"),
            NoStopRequested, _logger);

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, exitCode);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void AHostThatThrowsLogsExactlyOneErrorLineNamingTypeAndReason()
    {
        AdapterHostRunGuard.Run(
            () => throw new InvalidOperationException("datastore host mongo-0 does not resolve"),
            NoStopRequested, _logger);

        var errorLine = Assert.Single(ErrorLines);
        Assert.Contains(typeof(InvalidOperationException).FullName!, errorLine, StringComparison.Ordinal);
        Assert.Contains("datastore host mongo-0 does not resolve", errorLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAsyncHostThatThrowsEndsWithANonZeroExitCodeAndOneErrorLine()
    {
        var exitCode = await AdapterHostRunGuard.RunAsync(
            () => throw new InvalidOperationException("configuration does not bind"),
            NoStopRequested, _logger);

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, exitCode);
        var errorLine = Assert.Single(ErrorLines);
        Assert.Contains("configuration does not bind", errorLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAsyncHostThatFaultsAfterAwaitingEndsWithANonZeroExitCode()
    {
        async Task FaultingHost()
        {
            await Task.Yield();
            throw new InvalidOperationException("failed while starting");
        }

        var exitCode = await AdapterHostRunGuard.RunAsync(FaultingHost, NoStopRequested, _logger);

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, exitCode);
        Assert.Single(ErrorLines);
    }

    [Fact]
    public void AHostThatStopsNormallyEndsWithZeroAndLogsNoError()
    {
        var exitCode = AdapterHostRunGuard.Run(() => { }, NoStopRequested, _logger);

        Assert.Equal(0, exitCode);
        Assert.Empty(ErrorLines);
    }

    [Fact]
    public async Task AnAsyncHostThatStopsNormallyEndsWithZeroAndLogsNoError()
    {
        var exitCode = await AdapterHostRunGuard.RunAsync(() => Task.CompletedTask, NoStopRequested, _logger);

        Assert.Equal(0, exitCode);
        Assert.Empty(ErrorLines);
    }

    [Fact]
    public async Task ACancellationAfterAShutdownRequestEndsWithZero()
    {
        var exitCode = await AdapterHostRunGuard.RunAsync(
            () => throw new OperationCanceledException(), StopRequested, _logger);

        Assert.Equal(0, exitCode);
        Assert.Empty(ErrorLines);
    }

    [Fact]
    public void ACancellationWithoutAShutdownRequestIsAFailure()
    {
        var exitCode = AdapterHostRunGuard.Run(
            () => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            NoStopRequested, _logger);

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, exitCode);
        var errorLine = Assert.Single(ErrorLines);
        Assert.Contains(typeof(TaskCanceledException).FullName!, errorLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorThatIsNotACancellationIsAFailureEvenAfterAShutdownRequest()
    {
        var exitCode = AdapterHostRunGuard.Run(
            () => throw new InvalidOperationException("hosted service failed"), StopRequested, _logger);

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, exitCode);
        Assert.Single(ErrorLines);
    }

    [Fact]
    public async Task AFaultedBackgroundServiceIsRaisedWithItsOwnException()
    {
        var faultedService = new BackgroundServiceEndingWith(
            Task.FromException(new InvalidOperationException("background work failed")));
        await faultedService.StartAsync(CancellationToken.None);

        var raisedException = Assert.Throws<InvalidOperationException>(
            () => AdapterHostRunGuard.ThrowIfAnyBackgroundServiceFaulted([faultedService]));

        Assert.Equal("background work failed", raisedException.Message);
    }

    [Fact]
    public async Task CompletedAndCancelledBackgroundServicesAreNoFailure()
    {
        var completedService = new BackgroundServiceEndingWith(Task.CompletedTask);
        var cancelledService = new BackgroundServiceEndingWith(Task.FromCanceled(new CancellationToken(true)));
        var notStartedService = new BackgroundServiceEndingWith(Task.CompletedTask);
        await completedService.StartAsync(CancellationToken.None);
        await cancelledService.StartAsync(CancellationToken.None);

        AdapterHostRunGuard.ThrowIfAnyBackgroundServiceFaulted(
            [completedService, cancelledService, notStartedService, new PlainHostedService()]);
    }

    private sealed class BackgroundServiceEndingWith(Task outcome) : Microsoft.Extensions.Hosting.BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => outcome;
    }

    private sealed class PlainHostedService : Microsoft.Extensions.Hosting.IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
