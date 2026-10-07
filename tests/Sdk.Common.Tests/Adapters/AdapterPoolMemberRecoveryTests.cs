using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#4924 AP-I5 — member recovery, the adapter pool member's counterpart of AB#5409 (registration
///     state, readiness, recovery watchdog) and AB#5415 (CK model cache flush after a reconnect).
/// </summary>
/// <remarks>
///     A deaf pool member fails more quietly than a deaf dedicated adapter: it has no pipelines of
///     its own between leases, so a member the controller cannot reach simply never gets work while
///     its pod stays green. Every assertion here pins a behaviour whose absence logs nothing.
/// </remarks>
public class AdapterPoolMemberRecoveryTests
{
    private const string LeasedTenant = "borrower-a";
    private const string OtherTenant = "borrower-b";

    private static AdapterPoolMemberOptions ConfiguredPool() => new()
    {
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        MemberId = "octo-pool-0"
    };

    private static LeaseDto ALease(string leaseId = "lease-1", string tenantId = LeasedTenant) => new()
    {
        LeaseId = leaseId,
        TenantId = tenantId,
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        ClientId = "octo-pipeline-sa",
        ClientSecret = "secret",
        GrantedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
    };

    private static IOptions<AdapterOptions> AdapterOptionsWith(Action<AdapterOptions>? configure = null)
    {
        var options = new AdapterOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    /// <summary>A scriptable pool hub: what the registration answers, whether heartbeats are rejected.</summary>
    private sealed class ScriptedHubClient : IAdapterPoolHubClient
    {
        public Func<PoolMemberRegistrationDto, Task<PoolMemberRegistrationResultDto>> OnRegister { get; set; } =
            r => Task.FromResult(new PoolMemberRegistrationResultDto
            {
                Accepted = true, MemberId = r.MemberId, HeartbeatIntervalSeconds = 30
            });

        public Func<Exception?> HeartbeatFailure { get; set; } = () => null;

        public List<string> Calls { get; } = [];
        public List<PoolMemberHeartbeatDto> Heartbeats { get; } = [];
        public List<LeaseResultDto> Releases { get; } = [];
        public bool Alive { get; set; } = true;

        public Task<PoolMemberRegistrationResultDto> RegisterPoolMemberAsync(PoolMemberRegistrationDto registration)
        {
            Calls.Add("register");
            return OnRegister(registration);
        }

        public Task ReleaseLeaseAsync(LeaseResultDto result)
        {
            Calls.Add("release");
            Releases.Add(result);
            return Task.CompletedTask;
        }

        public Task HeartbeatAsync(PoolMemberHeartbeatDto heartbeat)
        {
            Calls.Add("heartbeat");
            Heartbeats.Add(heartbeat);
            var failure = HeartbeatFailure();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }

        public IServiceClientAccessToken ClientAccessToken { get; } = new ServiceClientAccessToken();
        public AdapterPoolHubClientOptions Options { get; } = new();
        public Uri? ServiceUri => new("https://controller.example.com/adapterPoolHub");
        public bool IsAlive => Alive;
        public void EnableReconnect(Func<bool, Task> onReconnectFunction) { }
        public Task StartAsync(Func<bool, Task> onConnectFunction, CancellationToken stoppingToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    /// <summary>A work item that holds the lease until the test lets go.</summary>
    private sealed class BlockingWorkItem : IAdapterLeaseWorkItem
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            return LeaseWorkOutcome.Succeeded();
        }
    }

    /// <summary>Records the CK model flushes, and which hub call had happened by then.</summary>
    private sealed class RecordingAdapterService(ScriptedHubClient hub) : IAdapterService
    {
        public List<string> Flushed { get; } = [];
        public bool Throw { get; set; }

        public Task<bool> StartupAsync(AdapterStartup adapterStartup, List<DeploymentUpdateErrorMessageDto> errorMessages,
            CancellationToken stoppingToken) => Task.FromResult(true);

        public Task ShutdownAsync(AdapterShutdown adapterShutdown, CancellationToken stoppingToken) => Task.CompletedTask;

        public Task CkModelChangedAsync(string tenantId)
        {
            hub.Calls.Add($"flush:{tenantId}");
            Flushed.Add(tenantId);
            return Throw ? Task.FromException(new InvalidOperationException("cache is gone")) : Task.CompletedTask;
        }
    }

    private sealed record Member(
        ScriptedHubClient Hub,
        AdapterPoolClient Client,
        AdapterPoolMemberService Service,
        RecordingAdapterService AdapterService,
        IAdapterHubRegistrationState State);

    private static Member CreateMember(IAdapterLeaseWorkItem? workItem = null)
    {
        var hub = new ScriptedHubClient();
        var state = new AdapterHubRegistrationState();
        var options = new OptionsWrapper<AdapterPoolMemberOptions>(ConfiguredPool());
        var client = new AdapterPoolClient(new AdapterPoolTenantScope(), hub, [],
            workItem ?? new NoAdapterLeaseWorkItem(), options, NullLogger<AdapterPoolClient>.Instance,
            registrationState: state);
        var adapterService = new RecordingAdapterService(hub);
        var service = new AdapterPoolMemberService(client, hub, options,
            NullLogger<AdapterPoolMemberService>.Instance, adapterService);
        return new Member(hub, client, service, adapterService, state);
    }

    // ---------------------------------------------------------------------------------------------
    // Registration state: written by the client on every attempt
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnAcceptedRegistration_MarksTheMemberRegistered()
    {
        var member = CreateMember();

        await member.Client.RegisterAsync();

        Assert.True(member.State.IsRegistered);
        Assert.True(member.State.HasEverRegistered);
        Assert.NotNull(member.State.LastRegisteredUtc);
    }

    /// <summary>
    ///     A controller pod that is shutting down answers <c>Accepted=false</c>. Before AP-I5 the member
    ///     logged a warning and sat on that connection, unregistered, until the pod went away — and if
    ///     it lingered, for ever. The refusal must be on the record so the next tick registers again.
    /// </summary>
    [Fact]
    public async Task ARefusedRegistration_LeavesTheMemberUnregistered_WithTheControllersReason()
    {
        var member = CreateMember();
        member.Hub.OnRegister = r => Task.FromResult(new PoolMemberRegistrationResultDto
        {
            Accepted = false, MemberId = r.MemberId, StatusMessage = "The controller instance is shutting down"
        });

        await member.Client.RegisterAsync();

        Assert.False(member.State.IsRegistered);
        Assert.Contains("shutting down", member.State.LastFailureMessage);
    }

    /// <summary>An enforcing tenant binding refuses with a HubException; that is a refusal too.</summary>
    [Fact]
    public async Task ARegistrationRejectedWithAHubException_LeavesTheMemberUnregistered()
    {
        var member = CreateMember();
        member.Hub.OnRegister = _ => Task.FromException<PoolMemberRegistrationResultDto>(
            new HubException("Pool member registration refused: presents a token of tenant 'x'"));

        var result = await member.Client.RegisterAsync();

        Assert.Null(result);
        Assert.False(member.State.IsRegistered);
        Assert.Contains("refused", member.State.LastFailureMessage);
    }

    /// <summary>
    ///     The AB#5409 failure — "InvokeCoreAsync cannot be called if the connection is not active".
    ///     It must reach the SignalR (re)connect loop, which retries; swallowing it would make the loop
    ///     take a failed registration for a successful connect (AB#4805).
    /// </summary>
    [Fact]
    public async Task ATransportFailureDuringRegistration_IsRecordedAndRethrown()
    {
        var member = CreateMember();
        await member.Client.RegisterAsync();
        member.Hub.OnRegister = _ => Task.FromException<PoolMemberRegistrationResultDto>(
            new InvalidOperationException("The 'InvokeCoreAsync' method cannot be called if the connection is not active"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => member.Client.RegisterAsync());

        Assert.False(member.State.IsRegistered);
        Assert.True(member.State.HasEverRegistered);
        Assert.Contains("InvokeCoreAsync", member.State.LastFailureMessage);
    }

    /// <summary>
    ///     🔴 A controller that has no registration for this connection rejects the heartbeat. That
    ///     is the only way a member can learn it was dropped while its connection stayed up.
    /// </summary>
    [Fact]
    public async Task ARejectedHeartbeat_ClearsTheRegistration()
    {
        var member = CreateMember();
        await member.Client.RegisterAsync();
        member.Hub.HeartbeatFailure = () => new HubException("This connection has no pool-member registration");

        await member.Client.HeartbeatAsync();

        Assert.False(member.State.IsRegistered);
        Assert.Contains("no pool-member registration", member.State.LastFailureMessage);
    }

    /// <summary>
    ///     The heartbeat names the LEASE it holds. Before AP-I5 it sent the leased tenant id under the
    ///     name <c>ActiveLeaseId</c>, which no reader could have matched against a lease.
    /// </summary>
    [Fact]
    public async Task TheHeartbeatNamesTheHeldLeaseById_AndNothingWhileIdle()
    {
        var workItem = new BlockingWorkItem();
        var member = CreateMember(workItem);
        await member.Client.RegisterAsync();

        await member.Client.HeartbeatAsync();
        Assert.Null(member.Hub.Heartbeats[^1].ActiveLeaseId);

        var leaseTask = member.Client.LeaseAsync(ALease("lease-42"));
        await workItem.Entered.Task;
        await member.Client.HeartbeatAsync();
        Assert.Equal("lease-42", member.Hub.Heartbeats[^1].ActiveLeaseId);

        workItem.Release.SetResult();
        await leaseTask;
        await member.Client.HeartbeatAsync();
        Assert.Null(member.Hub.Heartbeats[^1].ActiveLeaseId);
    }

    [Fact]
    public async Task RecentlyLeasedTenants_AreTheRunningLeaseAndTheLastOne_WithoutDuplicates()
    {
        var workItem = new BlockingWorkItem();
        var member = CreateMember(workItem);
        Assert.Empty(member.Client.RecentlyLeasedTenantIds);

        workItem.Release.SetResult();
        await member.Client.LeaseAsync(ALease("lease-1", OtherTenant));
        Assert.Equal([OtherTenant], member.Client.RecentlyLeasedTenantIds);
        Assert.Null(member.Client.CurrentLease);

        var blocking = new BlockingWorkItem();
        var busy = CreateMember(blocking);
        var leaseTask = busy.Client.LeaseAsync(ALease("lease-2", LeasedTenant));
        await blocking.Entered.Task;
        Assert.Equal("lease-2", busy.Client.CurrentLease?.LeaseId);
        Assert.Equal([LeasedTenant], busy.Client.RecentlyLeasedTenantIds);
        blocking.Release.SetResult();
        await leaseTask;
    }

    // ---------------------------------------------------------------------------------------------
    // Reconnect: CK model cache flush per recently leased tenant, deferred registration while busy
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     AB#5415 for members: <c>CkModelChanged</c> never reaches a member, so after a reconnect the
    ///     recently leased tenants' CK caches are dropped — and BEFORE the registration, so no new
    ///     lease can warm a cache that is about to be flushed.
    /// </summary>
    [Fact]
    public async Task AReconnect_FlushesTheRecentlyLeasedTenantsBeforeRegistering()
    {
        var workItem = new BlockingWorkItem();
        workItem.Release.SetResult();
        var member = CreateMember(workItem);
        await member.Service.OnConnectedAsync(false);
        await member.Client.LeaseAsync(ALease("lease-1", LeasedTenant));
        member.Hub.Calls.Clear();

        await member.Service.OnConnectedAsync(true);

        Assert.Equal([$"flush:{LeasedTenant}", "register"], member.Hub.Calls);
        Assert.True(member.State.IsRegistered);
    }

    /// <summary>The first connect has nothing to flush: the process has not cached anything yet.</summary>
    [Fact]
    public async Task TheFirstConnect_FlushesNothing()
    {
        var member = CreateMember();

        await member.Service.OnConnectedAsync(false);

        Assert.Empty(member.AdapterService.Flushed);
        Assert.Equal(["register"], member.Hub.Calls);
    }

    /// <summary>Best-effort, exactly like the dedicated flush: a failure must not cost the reconnect.</summary>
    [Fact]
    public async Task AFailingFlush_DoesNotPreventTheRegistration()
    {
        var workItem = new BlockingWorkItem();
        workItem.Release.SetResult();
        var member = CreateMember(workItem);
        await member.Client.LeaseAsync(ALease("lease-1", LeasedTenant));
        member.AdapterService.Throw = true;

        await member.Service.OnConnectedAsync(true);

        Assert.Equal([LeasedTenant], member.AdapterService.Flushed);
        Assert.True(member.State.IsRegistered);
    }

    /// <summary>
    ///     🔴 A member that reconnects while its lease still runs must not offer itself as free. The
    ///     controller already re-queued that lease; registering now would make it hand the member the
    ///     next lease at once, which the member must refuse — failing that borrower's execution for
    ///     nothing. The registration waits for the release, and the tick does it.
    /// </summary>
    [Fact]
    public async Task AReconnectDuringALease_DefersTheRegistrationUntilTheLeaseIsReleased()
    {
        var workItem = new BlockingWorkItem();
        var member = CreateMember(workItem);
        await member.Service.OnConnectedAsync(false);
        var leaseTask = member.Client.LeaseAsync(ALease("lease-7", LeasedTenant));
        await workItem.Entered.Task;
        member.Hub.Calls.Clear();

        await member.Service.OnConnectedAsync(true);

        // Flushed (the running lease's tenant may have missed a CK import), but not registered.
        Assert.Equal([$"flush:{LeasedTenant}"], member.Hub.Calls);
        Assert.False(member.State.IsRegistered);
        Assert.Contains("lease-7", member.State.LastFailureMessage);

        // While the lease runs, the tick neither heartbeats nor registers.
        await member.Service.TickAsync();
        Assert.Equal([$"flush:{LeasedTenant}"], member.Hub.Calls);

        workItem.Release.SetResult();
        await leaseTask;

        await member.Service.TickAsync();
        Assert.Equal("register", member.Hub.Calls[^1]);
        Assert.True(member.State.IsRegistered);
    }

    // ---------------------------------------------------------------------------------------------
    // Heartbeat tick: heartbeat while registered, re-register while not, nothing while disconnected
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheTick_HeartbeatsWhileRegistered()
    {
        var member = CreateMember();
        await member.Service.OnConnectedAsync(false);
        member.Hub.Calls.Clear();

        await member.Service.TickAsync();

        Assert.Equal(["heartbeat"], member.Hub.Calls);
    }

    /// <summary>
    ///     The self-heal that needs no restart: a refused registration (or a rejected heartbeat) is
    ///     followed by a fresh registration on the next tick rather than a heartbeat into nothing.
    /// </summary>
    [Fact]
    public async Task TheTick_RegistersAgainAfterARefusal()
    {
        var member = CreateMember();
        var refuse = true;
        member.Hub.OnRegister = r => Task.FromResult(new PoolMemberRegistrationResultDto
        {
            Accepted = !refuse, MemberId = r.MemberId, HeartbeatIntervalSeconds = 30,
            StatusMessage = refuse ? "The controller instance is shutting down" : null
        });
        await member.Service.OnConnectedAsync(false);
        Assert.False(member.State.IsRegistered);

        refuse = false;
        await member.Service.TickAsync();

        Assert.Equal(["register", "register"], member.Hub.Calls);
        Assert.True(member.State.IsRegistered);
    }

    [Fact]
    public async Task TheTick_RegistersAgainAfterARejectedHeartbeat()
    {
        var member = CreateMember();
        await member.Service.OnConnectedAsync(false);
        member.Hub.HeartbeatFailure = () => new HubException("no pool-member registration");

        await member.Service.TickAsync();
        Assert.False(member.State.IsRegistered);

        member.Hub.HeartbeatFailure = () => null;
        await member.Service.TickAsync();

        Assert.Equal(["register", "heartbeat", "register"], member.Hub.Calls);
        Assert.True(member.State.IsRegistered);
    }

    /// <summary>While the connection is down the SignalR client owns the reconnect; the tick stays out of it.</summary>
    [Fact]
    public async Task TheTick_DoesNothingWhileTheConnectionIsDown()
    {
        var member = CreateMember();
        member.Hub.Alive = false;

        await member.Service.TickAsync();

        Assert.Empty(member.Hub.Calls);
    }

    /// <summary>A tick that throws is a missed heartbeat, not a reason to end the member process.</summary>
    [Fact]
    public async Task TheTick_SwallowsATransportFailure()
    {
        var member = CreateMember();
        member.Hub.OnRegister = _ => Task.FromException<PoolMemberRegistrationResultDto>(
            new InvalidOperationException("connection is not active"));

        await member.Service.TickAsync();

        Assert.False(member.State.IsRegistered);
    }

    // ---------------------------------------------------------------------------------------------
    // Readiness: follows the pool-hub registration, never a lease
    // ---------------------------------------------------------------------------------------------

    private readonly IAdapterPoolHubClient _hubClient = A.Fake<IAdapterPoolHubClient>();
    private readonly AdapterHubRegistrationState _state = new();

    private Task<HealthCheckResult> CheckReadinessAsync(Action<AdapterOptions>? configure = null)
    {
        return new AdapterPoolHubReadinessHealthCheck(_hubClient, _state, AdapterOptionsWith(configure))
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    /// <summary>🔴 An idle member — no lease, no tenant — is ready as soon as it is registered.</summary>
    [Fact]
    public async Task Readiness_AnIdleRegisteredMember_IsReady()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        _state.MarkRegistered();

        var result = await CheckReadinessAsync(o => o.HubReadinessGracePeriod = TimeSpan.Zero);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("adapter pool hub", result.Description);
    }

    [Fact]
    public async Task Readiness_BeforeTheFirstRegistration_IsReadyWithinTheGracePeriod()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);

        var result = await CheckReadinessAsync(o => o.HubReadinessGracePeriod = TimeSpan.FromHours(1));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Readiness_NeverRegisteredAfterTheGracePeriod_IsNotReady()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);

        var result = await CheckReadinessAsync(o => o.HubReadinessGracePeriod = TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>Connected but unregistered — the deaf member — is not ready, and says why.</summary>
    [Fact]
    public async Task Readiness_ConnectedButRegistrationLost_IsNotReady_WithTheReason()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        _state.MarkRegistered();
        _state.MarkNotRegistered("The controller rejected a heartbeat: no pool-member registration");

        var result = await CheckReadinessAsync(o => o.HubReadinessGracePeriod = TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("no pool-member registration", result.Description);
    }

    [Fact]
    public async Task Readiness_RegisteredButDisconnected_IsNotReady()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        _state.MarkRegistered();

        var result = await CheckReadinessAsync(o => o.HubReadinessGracePeriod = TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Readiness_DisabledByConfiguration_IsReady()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);

        var result = await CheckReadinessAsync(o =>
        {
            o.HubReadinessProbeEnabled = false;
            o.HubReadinessGracePeriod = TimeSpan.Zero;
        });

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>A connection state that cannot be read is "not alive", never an exception out of the probe.</summary>
    [Fact]
    public async Task Readiness_AnUnreadableConnectionState_IsNotReady()
    {
        A.CallTo(() => _hubClient.IsAlive).Throws(new ObjectDisposedException("HubConnection"));
        _state.MarkRegistered();

        var result = await CheckReadinessAsync(o => o.HubReadinessGracePeriod = TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // ---------------------------------------------------------------------------------------------
    // Recovery watchdog: the same policy as AB#5409, sampling the pool hub
    // ---------------------------------------------------------------------------------------------

    private AdapterPoolHubRecoveryService CreateRecovery(IHostApplicationLifetime lifetime,
        Action<AdapterOptions>? configure = null)
    {
        return new AdapterPoolHubRecoveryService(_hubClient, _state, AdapterOptionsWith(configure),
            new AdapterLifetimeManagement(lifetime));
    }

    /// <summary>A member that never registered may face a pool that does not exist yet; a restart repairs nothing.</summary>
    [Fact]
    public void Recovery_NeverRegistered_NeverRestarts()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        var service = CreateRecovery(A.Fake<IHostApplicationLifetime>(),
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.Zero);

        var now = DateTime.UtcNow;

        Assert.False(service.Evaluate(now));
        Assert.False(service.Evaluate(now.AddHours(1)));
    }

    [Fact]
    public void Recovery_RegistrationLost_RestartsOnlyAfterTheTimeout()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        var service = CreateRecovery(A.Fake<IHostApplicationLifetime>(),
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.FromMinutes(15));
        var start = new DateTime(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc);
        _state.MarkRegistered();
        Assert.False(service.Evaluate(start));

        _state.MarkNotRegistered("The controller rejected a heartbeat");

        Assert.False(service.Evaluate(start.AddMinutes(1)));
        Assert.False(service.Evaluate(start.AddMinutes(15)));
        Assert.True(service.Evaluate(start.AddMinutes(16)));
    }

    [Fact]
    public void Recovery_RegistrationComesBack_ForgetsTheOutage()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(true);
        var service = CreateRecovery(A.Fake<IHostApplicationLifetime>(),
            o => o.HubRegistrationRecoveryTimeout = TimeSpan.FromMinutes(15));
        var start = new DateTime(2026, 10, 7, 6, 0, 0, DateTimeKind.Utc);
        _state.MarkRegistered();
        _state.MarkNotRegistered("re-registering after a reconnect");
        Assert.False(service.Evaluate(start));

        _state.MarkRegistered();

        Assert.False(service.Evaluate(start.AddMinutes(10)));
        Assert.Null(service.UnregisteredSinceUtc);
        Assert.False(service.Evaluate(start.AddMinutes(30)));
    }

    /// <summary>The end-to-end decision: a sustained outage stops the host so the pod restarts.</summary>
    [Fact]
    public async Task Recovery_SustainedOutage_StopsTheApplication()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        var lifetime = A.Fake<IHostApplicationLifetime>();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => lifetime.StopApplication()).Invokes(() => stopped.TrySetResult());
        _state.MarkRegistered();
        _state.MarkNotRegistered("connection lost");
        var service = CreateRecovery(lifetime, o => o.HubRegistrationRecoveryTimeout = TimeSpan.Zero);
        service.CheckInterval = TimeSpan.FromMilliseconds(10);

        await service.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(stopped.Task,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await service.StopAsync(CancellationToken.None);

        Assert.Same(stopped.Task, finished);
    }

    [Fact]
    public async Task Recovery_DisabledByConfiguration_NeverStopsTheApplication()
    {
        A.CallTo(() => _hubClient.IsAlive).Returns(false);
        var lifetime = A.Fake<IHostApplicationLifetime>();
        _state.MarkRegistered();
        _state.MarkNotRegistered("connection lost");
        var service = CreateRecovery(lifetime, o =>
        {
            o.HubRegistrationRecoveryEnabled = false;
            o.HubRegistrationRecoveryTimeout = TimeSpan.Zero;
        });
        service.CheckInterval = TimeSpan.FromMilliseconds(10);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await service.StopAsync(CancellationToken.None);

        A.CallTo(() => lifetime.StopApplication()).MustNotHaveHappened();
    }

    // ---------------------------------------------------------------------------------------------
    // Composition: one registration state, shared by the client and whatever reads it
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     🔴 The client writes the state and the readiness check / watchdog read it. Two instances
    ///     would give a member that registers fine and is reported deaf for ever.
    /// </summary>
    [Fact]
    public async Task TheMemberComposition_SharesOneRegistrationStateWithTheClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAdapterPoolMember();
        await using var provider = services.BuildServiceProvider();

        var state = provider.GetRequiredService<IAdapterHubRegistrationState>();
        var client = provider.GetRequiredService<AdapterPoolClient>();

        Assert.Same(state, client.RegistrationState);
    }
}
