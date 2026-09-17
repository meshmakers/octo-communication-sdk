using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#4924 — the hosted service that turns a configured process into a running pool member.
/// </summary>
/// <remarks>
///     Increments 6 and 7 built the client, the hub, the scheduler and the work item, but nothing
///     composed them into a process. The failure mode of getting this wrong is silence — a member
///     that is connected, healthy-looking and never leased — so every assertion here is about a
///     behaviour whose absence produces no error anywhere.
/// </remarks>
public class AdapterPoolMemberServiceTests
{
    private static AdapterPoolMemberOptions ConfiguredPool() => new()
    {
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        MemberId = "octo-pool-0"
    };

    /// <summary>Hub client that captures the connect callback so a test can drive (re)connects.</summary>
    private sealed class CapturingHubClient : IAdapterPoolHubClient
    {
        private readonly int _heartbeatSeconds;

        public CapturingHubClient(int heartbeatSeconds = 30) => _heartbeatSeconds = heartbeatSeconds;

        public Func<bool, Task>? OnConnect { get; private set; }
        public Func<bool, Task>? OnReconnect { get; private set; }
        public int RegistrationCount { get; private set; }
        public int HeartbeatCount { get; private set; }
        public int StopCount { get; private set; }

        public Task<PoolMemberRegistrationResultDto> RegisterPoolMemberAsync(PoolMemberRegistrationDto registration)
        {
            RegistrationCount++;
            return Task.FromResult(new PoolMemberRegistrationResultDto
            {
                Accepted = true,
                MemberId = registration.MemberId,
                HeartbeatIntervalSeconds = _heartbeatSeconds
            });
        }

        public Task ReleaseLeaseAsync(LeaseResultDto result) => Task.CompletedTask;

        public Task HeartbeatAsync(PoolMemberHeartbeatDto heartbeat)
        {
            HeartbeatCount++;
            return Task.CompletedTask;
        }

        public IServiceClientAccessToken ClientAccessToken { get; } = new ServiceClientAccessToken();
        public AdapterPoolHubClientOptions Options { get; } = new();
        public Uri? ServiceUri => new("https://controller.example.com/adapterPoolHub");
        public bool IsAlive => true;

        public void EnableReconnect(Func<bool, Task> onReconnectFunction) => OnReconnect = onReconnectFunction;

        public Task StartAsync(Func<bool, Task> onConnectFunction, CancellationToken stoppingToken)
        {
            OnConnect = onConnectFunction;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>A hub client whose connection attempt fails, as one does when the controller is down.</summary>
    private sealed class FailingHubClient : IAdapterPoolHubClient
    {
        public Task<PoolMemberRegistrationResultDto> RegisterPoolMemberAsync(PoolMemberRegistrationDto r) =>
            Task.FromResult(new PoolMemberRegistrationResultDto());

        public Task ReleaseLeaseAsync(LeaseResultDto result) => Task.CompletedTask;
        public Task HeartbeatAsync(PoolMemberHeartbeatDto heartbeat) => Task.CompletedTask;
        public IServiceClientAccessToken ClientAccessToken { get; } = new ServiceClientAccessToken();
        public AdapterPoolHubClientOptions Options { get; } = new();
        public Uri? ServiceUri => null;
        public bool IsAlive => false;
        public void EnableReconnect(Func<bool, Task> onReconnectFunction) { }

        public Task StartAsync(Func<bool, Task> onConnectFunction, CancellationToken stoppingToken) =>
            throw new InvalidOperationException("controller unreachable");

        public Task StopAsync() => Task.CompletedTask;
    }

    private static AdapterPoolMemberService CreateService(IAdapterPoolHubClient hubClient,
        AdapterPoolMemberOptions options)
    {
        var scope = new AdapterPoolTenantScope();
        var poolClient = new AdapterPoolClient(scope, hubClient, [], new NoAdapterLeaseWorkItem(),
            new OptionsWrapper<AdapterPoolMemberOptions>(options),
            NullLogger<AdapterPoolClient>.Instance);

        return new AdapterPoolMemberService(poolClient, hubClient,
            new OptionsWrapper<AdapterPoolMemberOptions>(options),
            NullLogger<AdapterPoolMemberService>.Instance);
    }

    /// <summary>
    ///     The host starts a BackgroundService without waiting for it to reach its first real await,
    ///     so a test that asserted immediately after StartAsync would be racing the scheduler rather
    ///     than testing the service. Poll instead of sleeping a fixed amount.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}");
    }

    /// <summary>
    ///     🔴 The controller binds a member to its SignalR <i>connection</i>. Registering once after
    ///     StartAsync would leave a reconnected member connected and never leased again, with nothing
    ///     logged. So registration has to be the connect callback itself.
    /// </summary>
    [Fact]
    public async Task RegistersOnEveryConnect_NotOnlyTheFirst()
    {
        var hub = new CapturingHubClient();
        var service = CreateService(hub, ConfiguredPool());
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await WaitUntil(() => hub.OnConnect is not null, "the management connection to open");

        // Registration must BE the connect callback, not a line of startup code that happens to run
        // once — only the callback is replayed on a reconnect.
        Assert.Equal(0, hub.RegistrationCount);

        await hub.OnConnect!(false);
        Assert.Equal(1, hub.RegistrationCount);

        // The same callback must also be the one handed to the reconnect path.
        Assert.NotNull(hub.OnReconnect);
        await hub.OnReconnect!(true);
        Assert.Equal(2, hub.RegistrationCount);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     The cadence comes from the controller's registration answer, so the two sides cannot drift
    ///     apart. A local default would be a second, quietly different policy.
    /// </summary>
    [Fact]
    public async Task AdoptsTheHeartbeatCadenceTheControllerReturns()
    {
        var hub = new CapturingHubClient(7);
        var service = CreateService(hub, ConfiguredPool());
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await WaitUntil(() => hub.OnConnect is not null, "the management connection to open");
        Assert.Equal(AdapterPoolMemberService.FallbackHeartbeatInterval, service.HeartbeatInterval);

        await hub.OnConnect!(false);

        Assert.Equal(TimeSpan.FromSeconds(7), service.HeartbeatInterval);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     A controller that answers with nothing (an older one, or a refusal) must not collapse the
    ///     cadence to zero — that would be a tight heartbeat loop against the controller.
    /// </summary>
    [Fact]
    public async Task AnAnswerWithoutACadence_KeepsTheFallback()
    {
        var hub = new CapturingHubClient(0);
        var service = CreateService(hub, ConfiguredPool());
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await WaitUntil(() => hub.OnConnect is not null, "the management connection to open");
        await hub.OnConnect!(false);

        Assert.Equal(AdapterPoolMemberService.FallbackHeartbeatInterval, service.HeartbeatInterval);

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     🔴 AB#5080: an unhandled exception out of a BackgroundService stops the host. A controller
    ///     that is not up yet must not take the member process down with it.
    /// </summary>
    [Fact]
    public async Task AnUnreachableController_DoesNotTakeTheHostDown()
    {
        var service = CreateService(new FailingHubClient(), ConfiguredPool());
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // ExecuteAsync must have completed by swallowing, not by faulting.
        Assert.NotNull(service.ExecuteTask);
        await service.ExecuteTask!;
        Assert.False(service.ExecuteTask!.IsFaulted);

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    ///     Defensive: the builder only registers the service when the section is set, but a host that
    ///     registers it by hand with no pool configured should idle rather than connect to nothing.
    /// </summary>
    [Fact]
    public async Task WithoutAConfiguredPool_ItDoesNothing()
    {
        var hub = new CapturingHubClient();
        var service = CreateService(hub, new AdapterPoolMemberOptions());
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Bounded rather than awaiting ExecuteTask: without the guard the service enters its
        // heartbeat loop and that task never completes, which would hang this test instead of
        // failing it.
        var completed = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Same(service.ExecuteTask, completed);
        Assert.Null(hub.OnConnect);
        Assert.Equal(0, hub.RegistrationCount);

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>The management connection is closed on shutdown rather than left to the process exit.</summary>
    [Fact]
    public async Task OnShutdown_TheManagementConnectionIsClosed()
    {
        var hub = new CapturingHubClient();
        var service = CreateService(hub, ConfiguredPool());
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await WaitUntil(() => hub.OnConnect is not null, "the management connection to open");
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, hub.StopCount);
    }
}
