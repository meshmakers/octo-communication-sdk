using Meshmakers.Octo.Sdk.Common.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     A background service that faults or cancels itself must fail the adapter run, so the process ends
///     with a non-zero exit code; a stop that was asked for must not.
/// </summary>
public sealed class BackgroundServiceWatchTests
{
    private const string FailureReason = "The request was canceled due to the configured HttpClient.Timeout";

    private static IHost BuildHost(Action<IServiceCollection> registerServices)
    {
        var hostBuilder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        registerServices(hostBuilder.Services);
        return hostBuilder.Build();
    }

    private static async Task RunWatchedAsync(IHost host, bool runsAsync)
    {
        var backgroundServiceWatch = BackgroundServiceWatch.For(host.Services);
        if (runsAsync)
        {
            await backgroundServiceWatch.RunAsync(() => host.RunAsync());
        }
        else
        {
            backgroundServiceWatch.Run(host.Run);
        }
    }

    private static Task RunWatchedWithinTimeoutAsync(IHost host) =>
        RunWatchedAsync(host, true).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(typeof(FaultingService), false)]
    [InlineData(typeof(FaultingService), true)]
    [InlineData(typeof(CancelsItselfAfterStartService), false)]
    [InlineData(typeof(CancelsItselfAfterStartService), true)]
    [InlineData(typeof(FaultWrappingACancellationService), false)]
    [InlineData(typeof(FaultThatIsACancellationService), false)]
    public async Task ABackgroundServiceThatEndsOnItsOwnFailsTheRunNamingServiceAndReason(Type serviceType,
        bool runsAsync)
    {
        using var host = BuildHost(services => services.AddSingleton(typeof(IHostedService), serviceType));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunWatchedAsync(host, runsAsync));

        Assert.Contains(serviceType.FullName!, failure.Message, StringComparison.Ordinal);
        Assert.Contains(FailureReason, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFaultWhileAnotherServiceIsStillStartingCancelsTheStartAndFailsTheRun()
    {
        using var host = BuildHost(services =>
        {
            services.AddHostedService<FaultingService>();
            services.AddHostedService<StartsUntilCancelledService>();
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunWatchedWithinTimeoutAsync(host));

        Assert.Contains(typeof(FaultingService).FullName!, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHostedServiceTheWatchCannotSeeLeavesStoppingToTheHost()
    {
        using var host = BuildHost(services =>
        {
            services.AddTransient<IHostedService, FaultingService>();
            services.AddHostedService<RunsUntilStoppedService>();
        });

        var hostOptions = host.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        await RunWatchedWithinTimeoutAsync(host);

        Assert.Equal(BackgroundServiceExceptionBehavior.StopHost, hostOptions.BackgroundServiceExceptionBehavior);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStopThatWasAskedForDoesNotFailTheRunWhateverTheCallbackOrder(
        bool endsOnStoppingBeforeTheWatchExists)
    {
        using var host = BuildHost(services =>
        {
            services.AddSingleton(new StoppingCallbackOrder(endsOnStoppingBeforeTheWatchExists));
            services.AddSingleton<IHostedService>(_ => new RunsUntilStoppedService());
            services.AddHostedService<EndsOnApplicationStoppingService>();
            services.AddSingleton<IHostedService>(_ => new RunsUntilStoppedService());
            services.AddHostedService<StopsWhenStartedService>();
        });

        var runFailure = await Record.ExceptionAsync(() => RunWatchedAsync(host, false));

        Assert.Null(runFailure);
    }

    [Fact]
    public async Task AnAdapterThatIgnoresBackgroundFailuresKeepsRunningAfterAFaultAndFailsWhenItStops()
    {
        var faultObservation = new FaultObservation();
        using var host = BuildHost(services =>
        {
            services.Configure<HostOptions>(hostOptions =>
                hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
            services.AddSingleton(faultObservation);
            services.AddHostedService<FaultingService>();
            services.AddHostedService<StopsAfterAFaultService>();
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => RunWatchedAsync(host, false));

        Assert.False(faultObservation.StopRequestedByTheFault);
        Assert.Contains(typeof(FaultingService).FullName!, failure.Message, StringComparison.Ordinal);
    }

    private static FaultingService FaultingServiceOf(IServiceProvider serviceProvider) =>
        serviceProvider.GetServices<IHostedService>().OfType<FaultingService>().Single();

    private sealed class FaultingService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new HttpRequestException(FailureReason);
        }
    }

    private sealed class CancelsItselfAfterStartService : BackgroundService
    {
        private readonly TaskCompletionSource _hostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancelsItselfAfterStartService(IHostApplicationLifetime applicationLifetime)
        {
            applicationLifetime.ApplicationStarted.Register(() => _hostStarted.SetResult());
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _hostStarted.Task;
            throw new TaskCanceledException(FailureReason, new TimeoutException());
        }
    }

    private sealed class FaultWrappingACancellationService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException(FailureReason, new OperationCanceledException());
        }
    }

    private sealed class FaultThatIsACancellationService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.FromException(new OperationCanceledException(FailureReason));
    }

    private sealed class StartsUntilCancelledService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FaultObservation
    {
        internal bool StopRequestedByTheFault { get; set; }
    }

    private sealed class StopsAfterAFaultService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly FaultObservation _faultObservation;
        private readonly TaskCompletionSource _hostStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StopsAfterAFaultService(IServiceProvider serviceProvider,
            IHostApplicationLifetime applicationLifetime, FaultObservation faultObservation)
        {
            _serviceProvider = serviceProvider;
            _applicationLifetime = applicationLifetime;
            _faultObservation = faultObservation;
            applicationLifetime.ApplicationStarted.Register(() => _hostStarted.SetResult());
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.WhenAll(_hostStarted.Task, Task.WhenAny(FaultingServiceOf(_serviceProvider).ExecuteTask!));
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
            _faultObservation.StopRequestedByTheFault = _applicationLifetime.ApplicationStopping.IsCancellationRequested;
            _applicationLifetime.StopApplication();
        }
    }

    private sealed class RunsUntilStoppedService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private sealed record StoppingCallbackOrder(bool RegisterBeforeTheWatchExists);

    private sealed class EndsOnApplicationStoppingService : BackgroundService
    {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly StoppingCallbackOrder _stoppingCallbackOrder;
        private readonly CancellationTokenSource _ownCancellation = new();

        public EndsOnApplicationStoppingService(IHostApplicationLifetime applicationLifetime,
            StoppingCallbackOrder stoppingCallbackOrder)
        {
            _applicationLifetime = applicationLifetime;
            _stoppingCallbackOrder = stoppingCallbackOrder;
            if (stoppingCallbackOrder.RegisterBeforeTheWatchExists)
            {
                RegisterOwnCancellationOnStopping();
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_stoppingCallbackOrder.RegisterBeforeTheWatchExists)
            {
                RegisterOwnCancellationOnStopping();
            }

            return Task.Delay(Timeout.Infinite, _ownCancellation.Token);
        }

        public override void Dispose()
        {
            _ownCancellation.Dispose();
            base.Dispose();
        }

        private void RegisterOwnCancellationOnStopping() =>
            _applicationLifetime.ApplicationStopping.Register(_ownCancellation.Cancel);
    }

    private sealed class StopsWhenStartedService : IHostedService
    {
        public StopsWhenStartedService(IHostApplicationLifetime applicationLifetime)
        {
            applicationLifetime.ApplicationStarted.Register(applicationLifetime.StopApplication);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
