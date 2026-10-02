using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5409: the readiness probe and the recovery watchdog must both be driven by the adapter hub
///     REGISTRATION, not by "the socket is not Disconnected" and not by the generic 15-second startup
///     task that <c>/healthz/ready</c> used to be the only gate on.
/// </summary>
public class AdapterHubRegistrationReadinessTests
{
    private readonly IAdapterHubClient _hubClient = A.Fake<IAdapterHubClient>();
    private readonly AdapterHubRegistrationState _registrationState = new();

    private static IOptions<AdapterOptions> Options(Action<AdapterOptions>? configure = null)
    {
        var options = new AdapterOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private AdapterHubReadinessHealthCheck CreateHealthCheck(Action<AdapterOptions>? configure = null)
    {
        return new AdapterHubReadinessHealthCheck(_hubClient, _registrationState, Options(configure));
    }

    private static Task<HealthCheckResult> CheckAsync(AdapterHubReadinessHealthCheck check)
    {
        return check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    [Fact]
    public async Task Readiness_WhenRegisteredAndConnected_IsHealthy()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        _registrationState.MarkRegistered();

        var result = await CheckAsync(CreateHealthCheck());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>
    ///     The core regression: no hub connection at all must NOT answer ready once the startup grace
    ///     period is over. Before the fix this returned 200 for eleven hours on five prod-1 pods.
    /// </summary>
    [Fact]
    public async Task Readiness_WithoutHubConnection_IsUnhealthyAfterGracePeriod()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);

        var result = await CheckAsync(CreateHealthCheck(o => o.HubReadinessGracePeriod = TimeSpan.Zero));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>
    ///     "Connected" is not "reachable": the SignalR connection can be up while the controller has
    ///     no registration for this adapter, which is exactly the state that answered every deploy
    ///     with "has no live SignalR connection".
    /// </summary>
    [Fact]
    public async Task Readiness_WhenConnectedButRegistrationFailed_IsUnhealthy()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        _registrationState.MarkRegistered();
        _registrationState.MarkNotRegistered("connection is not active");

        var result = await CheckAsync(CreateHealthCheck(o => o.HubReadinessGracePeriod = TimeSpan.Zero));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("connection is not active", result.Description);
    }

    /// <summary>
    ///     A fresh pod must not be taken out of service before it ever had a chance to register -
    ///     the controller may still be rolling out, the tenant may not be enabled yet.
    /// </summary>
    [Fact]
    public async Task Readiness_BeforeFirstRegistration_IsHealthyWithinGracePeriod()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);

        var result = await CheckAsync(CreateHealthCheck(o => o.HubReadinessGracePeriod = TimeSpan.FromHours(1)));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Readiness_WhenDisabledByConfiguration_IsHealthy()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);

        var result = await CheckAsync(CreateHealthCheck(o =>
        {
            o.HubReadinessProbeEnabled = false;
            o.HubReadinessGracePeriod = TimeSpan.Zero;
        }));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private AdapterHubRecoveryService CreateRecoveryService(IHostApplicationLifetime lifetime,
        Action<AdapterOptions>? configure = null)
    {
        return new AdapterHubRecoveryService(_hubClient, _registrationState, Options(configure),
            new AdapterLifetimeManagement(lifetime));
    }

    [Fact]
    public void Recovery_WhenNeverRegistered_NeverRestarts()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        var service = CreateRecoveryService(A.Fake<IHostApplicationLifetime>(),
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.Zero);

        var now = DateTime.UtcNow;

        Assert.False(service.Evaluate(now));
        Assert.False(service.Evaluate(now.AddHours(1)));
        Assert.Null(service.UnregisteredSinceUtc);
    }

    [Fact]
    public void Recovery_WhenRegistrationLost_RestartsOnlyAfterTimeout()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        var service = CreateRecoveryService(A.Fake<IHostApplicationLifetime>(),
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.FromMinutes(15));

        var start = new DateTime(2026, 9, 29, 6, 0, 0, DateTimeKind.Utc);
        _registrationState.MarkRegistered();
        Assert.False(service.Evaluate(start));

        _registrationState.MarkNotRegistered("connection is not active");

        // First observation only starts the clock.
        Assert.False(service.Evaluate(start));
        Assert.Equal(start, service.UnregisteredSinceUtc);

        // A short outage must not cost a restart.
        Assert.False(service.Evaluate(start.AddMinutes(14)));

        // A sustained one must.
        Assert.True(service.Evaluate(start.AddMinutes(15)));
    }

    [Fact]
    public void Recovery_WhenRegistrationComesBack_ForgetsTheOutage()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        var service = CreateRecoveryService(A.Fake<IHostApplicationLifetime>(),
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.FromMinutes(15));

        var start = new DateTime(2026, 9, 29, 6, 0, 0, DateTimeKind.Utc);
        _registrationState.MarkRegistered();
        _registrationState.MarkNotRegistered("connection is not active");
        Assert.False(service.Evaluate(start));

        _registrationState.MarkRegistered();
        Assert.False(service.Evaluate(start.AddMinutes(20)));
        Assert.Null(service.UnregisteredSinceUtc);
    }

    [Fact]
    public async Task Recovery_WhenSustainedOutage_StopsTheApplication()
    {
        var lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        var service = CreateRecoveryService(lifetime, o => o.HubRegistrationRecoveryTimeout = TimeSpan.Zero);
        service.CheckInterval = TimeSpan.FromMilliseconds(10);

        _registrationState.MarkRegistered();
        _registrationState.MarkNotRegistered("connection is not active");

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Fake.GetCalls(lifetime).Any(c => c.Method.Name == nameof(IHostApplicationLifetime.StopApplication)))
                {
                    break;
                }

                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        A.CallTo(() => lifetime.StopApplication()).MustHaveHappened();
    }

    /// <summary>
    ///     AB#5473: the check runs on its own timer, so it also samples the hub client while a tenant
    ///     update has it stopped. A check that fails must not end the service - an exception that
    ///     escapes a background service stops the host, which restarted the adapter on roughly every
    ///     sixth tenant update.
    /// </summary>
    [Fact]
    public async Task Recovery_WhenTheCheckThrowsWhileTheHubClientIsStopped_KeepsRunning()
    {
        var lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => _hubClient.IsAlive)
            .Throws(new ObjectDisposedException("HubConnection", "The SignalR client is stopping."));
        var service = CreateRecoveryService(lifetime,
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.FromMinutes(15));
        service.CheckInterval = TimeSpan.FromMilliseconds(10);

        _registrationState.MarkRegistered();

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && HubClientIsAliveReads() < 3)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            // Several failing checks later the service is still sampling.
            Assert.True(HubClientIsAliveReads() >= 3);
            Assert.False(service.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        A.CallTo(() => lifetime.StopApplication()).MustNotHaveHappened();
    }

    /// <summary>
    ///     The other half of AB#5473: surviving a failing check must not disarm the watchdog. An
    ///     adapter that stays unregistered beyond the timeout is still restarted.
    /// </summary>
    [Fact]
    public async Task Recovery_WhenTheCheckThrows_StillStopsTheApplicationOnSustainedOutage()
    {
        var lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => _hubClient.IsAlive)
            .Throws(new ObjectDisposedException("HubConnection", "The SignalR client is stopping."));
        var service = CreateRecoveryService(lifetime, o => o.HubRegistrationRecoveryTimeout = TimeSpan.Zero);
        service.CheckInterval = TimeSpan.FromMilliseconds(10);

        _registrationState.MarkRegistered();
        _registrationState.MarkNotRegistered("adapter execution service stopped");

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Fake.GetCalls(lifetime).Any(c => c.Method.Name == nameof(IHostApplicationLifetime.StopApplication)))
                {
                    break;
                }

                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        A.CallTo(() => lifetime.StopApplication()).MustHaveHappened();
    }

    private int HubClientIsAliveReads()
    {
        return Fake.GetCalls(_hubClient).Count(c => c.Method.Name == "get_IsAlive");
    }

    [Fact]
    public async Task Recovery_WhenDisabledByConfiguration_NeverStopsTheApplication()
    {
        var lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        var service = CreateRecoveryService(lifetime, o =>
        {
            o.HubRegistrationRecoveryEnabled = false;
            o.HubRegistrationRecoveryTimeout = TimeSpan.Zero;
        });
        service.CheckInterval = TimeSpan.FromMilliseconds(10);

        _registrationState.MarkRegistered();
        _registrationState.MarkNotRegistered("connection is not active");

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await service.StopAsync(CancellationToken.None);

        A.CallTo(() => lifetime.StopApplication()).MustNotHaveHappened();
    }
}
