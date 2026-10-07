using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5864 — a draining pool member ends as a process: it keeps the outcome of work it already
///     ran, never registers again, is not ready, and exits once idle so the pool replaces it.
/// </summary>
/// <remarks>
///     The test-2-dev repro (2026-10-07): a lease participant failed to leave, the member drained
///     itself, stayed <c>1/1 Ready</c> and registered, refused every lease it was then granted, and
///     the pool was dead until the pod was deleted by hand.
/// </remarks>
public class AdapterPoolMemberDrainTests
{
    private static LeaseDto ALease(string leaseId = "lease-1") => new()
    {
        LeaseId = leaseId,
        TenantId = "borrower-a",
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        ClientId = "octo-pipeline-sa",
        ClientSecret = "secret",
        GrantedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
    };

    private sealed class RecordingHub : IAdapterPoolHubClient
    {
        public List<string> Calls { get; } = [];
        public List<LeaseResultDto> Releases { get; } = [];

        /// <summary>Lets a test hold the release report in flight.</summary>
        public TaskCompletionSource? HoldRelease { get; set; }

        public Task<PoolMemberRegistrationResultDto> RegisterPoolMemberAsync(PoolMemberRegistrationDto registration)
        {
            Calls.Add("register");
            return Task.FromResult(new PoolMemberRegistrationResultDto
            {
                Accepted = true, MemberId = registration.MemberId, HeartbeatIntervalSeconds = 30
            });
        }

        public async Task ReleaseLeaseAsync(LeaseResultDto result)
        {
            Calls.Add("release");
            Releases.Add(result);
            if (HoldRelease is not null)
            {
                await HoldRelease.Task;
            }
        }

        public Task HeartbeatAsync(PoolMemberHeartbeatDto heartbeat)
        {
            Calls.Add("heartbeat");
            return Task.CompletedTask;
        }

        public IServiceClientAccessToken ClientAccessToken { get; } = new ServiceClientAccessToken();
        public AdapterPoolHubClientOptions Options { get; } = new();
        public Uri? ServiceUri => new("https://controller.example.com/adapterPoolHub");
        public bool IsAlive => true;
        public void EnableReconnect(Func<bool, Task> onReconnectFunction) { }
        public Task StartAsync(Func<bool, Task> onConnectFunction, CancellationToken stoppingToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class FailingLeaveParticipant : IAdapterLeaseParticipant
    {
        public Task EnterLeaseAsync(LeaseDto lease, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LeaveLeaseAsync(LeaseDto lease, CancellationToken cancellationToken) =>
            Task.FromException(new TimeoutException("trigger unregistration failed for node 'FromPipelineTriggerEvent@1'"));
    }

    private sealed class WorkItem(LeaseWorkOutcome outcome) : IAdapterLeaseWorkItem
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Hold { get; init; }

        public async Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (Hold is not null)
            {
                await Hold.Task;
            }

            return outcome;
        }
    }

    private sealed record Member(RecordingHub Hub, AdapterPoolClient Client, AdapterPoolMemberService Service,
        IAdapterHubRegistrationState State, OptionsWrapper<AdapterPoolMemberOptions> Options);

    private static Member CreateMember(IAdapterLeaseWorkItem? workItem = null,
        IEnumerable<IAdapterLeaseParticipant>? participants = null, bool exitWhenDrained = true)
    {
        var hub = new RecordingHub();
        var state = new AdapterHubRegistrationState();
        var options = new OptionsWrapper<AdapterPoolMemberOptions>(new AdapterPoolMemberOptions
        {
            AdapterPoolTenantId = "lender",
            AdapterPoolRtId = "665f0000000000000000ee21",
            MemberId = "octo-pool-0",
            ExitWhenDrained = exitWhenDrained
        });
        var client = new AdapterPoolClient(new AdapterPoolTenantScope(), hub, participants ?? [],
            workItem ?? new NoAdapterLeaseWorkItem(), options, NullLogger<AdapterPoolClient>.Instance,
            registrationState: state);
        var service = new AdapterPoolMemberService(client, hub, options, NullLogger<AdapterPoolMemberService>.Instance);
        return new Member(hub, client, service, state, options);
    }

    private static (AdapterPoolMemberDrainExitService Service, IHostApplicationLifetime Lifetime) CreateExit(Member member)
    {
        var lifetime = A.Fake<IHostApplicationLifetime>();
        return (new AdapterPoolMemberDrainExitService(member.Client, member.Options,
            new AdapterLifetimeManagement(lifetime)), lifetime);
    }

    // ---------------------------------------------------------------------------------------------
    // The work item's outcome survives the drain
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     🔴 The repro's first half: the pipeline COMPLETED, then a participant failed to leave, and
    ///     the execution was set to FAILED. The failed leave is a fact about the process; the work
    ///     item's success and output travel with the Drained reason.
    /// </summary>
    [Fact]
    public async Task AFailedLeave_KeepsTheWorkItemsSuccessAndOutput_AndReportsDrained()
    {
        var member = CreateMember(new WorkItem(LeaseWorkOutcome.Succeeded("done", "{\"ok\":true}")),
            [new FailingLeaveParticipant()]);

        await member.Client.LeaseAsync(ALease());

        var release = Assert.Single(member.Hub.Releases);
        Assert.Equal(LeaseReleaseReasonDto.Drained, release.Reason);
        Assert.True(release.Success);
        Assert.Equal("{\"ok\":true}", release.OutputData);
        Assert.NotNull(release.WorkDurationMs);
        Assert.StartsWith("done ", release.StatusMessage);
        Assert.Contains("FromPipelineTriggerEvent@1", release.StatusMessage);
        Assert.Contains("FailingLeaveParticipant", member.Client.DrainReason);
    }

    [Fact]
    public async Task AFailedLeave_AfterAFailedWorkItem_StillReportsTheWorkItemsFailure()
    {
        var member = CreateMember(new WorkItem(LeaseWorkOutcome.Failed("node 3 threw")),
            [new FailingLeaveParticipant()]);

        await member.Client.LeaseAsync(ALease());

        var release = Assert.Single(member.Hub.Releases);
        Assert.Equal(LeaseReleaseReasonDto.Drained, release.Reason);
        Assert.False(release.Success);
        Assert.StartsWith("node 3 threw", release.StatusMessage);
    }

    // ---------------------------------------------------------------------------------------------
    // Drained and idle — the exit condition
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ANewMember_IsNotDrainedAndIdle()
    {
        var member = CreateMember();

        Assert.False(member.Client.IsDraining);
        Assert.False(member.Client.IsDrainedAndIdle);
        Assert.Null(member.Client.DrainReason);
    }

    /// <summary>
    ///     A drain that arrives during a lease lets the lease finish — and the member is only idle
    ///     once the release has been REPORTED, not merely once the tenant left the process. Exiting in
    ///     between would turn a completed run into an interrupted one at the controller, which then
    ///     runs it again.
    /// </summary>
    [Fact]
    public async Task ADrainDuringALease_IsIdleOnlyAfterTheReleaseWasReported()
    {
        var work = new WorkItem(LeaseWorkOutcome.Succeeded()) { Hold = new TaskCompletionSource() };
        var member = CreateMember(work);
        member.Hub.HoldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var lease = member.Client.LeaseAsync(ALease());
        await work.Entered.Task;
        var drain = member.Client.DrainAsync("pool idle");

        Assert.True(member.Client.IsDraining);
        Assert.False(member.Client.IsDrainedAndIdle);

        work.Hold.SetResult();
        while (member.Hub.Releases.Count == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        // The tenant is gone (CurrentLease is null) but the release is still on the wire.
        Assert.False(member.Client.IsDrainedAndIdle);

        member.Hub.HoldRelease.SetResult();
        await lease;
        await drain;

        Assert.True(member.Client.IsDrainedAndIdle);
        Assert.Equal(LeaseReleaseReasonDto.Drained, member.Hub.Releases[0].Reason);
        Assert.Contains("pool idle", member.Client.DrainReason);
    }

    // ---------------------------------------------------------------------------------------------
    // A draining member never registers again
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADrainingMember_DoesNotRegisterOnTheTick()
    {
        var member = CreateMember();
        await member.Service.OnConnectedAsync(false);
        member.State.MarkNotRegistered("The controller rejected a heartbeat");
        await member.Client.DrainAsync("its lease expired");
        member.Hub.Calls.Clear();

        await member.Service.TickAsync();

        Assert.DoesNotContain("register", member.Hub.Calls);
    }

    /// <summary>
    ///     🔴 The second half of the repro's loop: a registration on a NEW connection starts as
    ///     available in the controller's registry, so a draining member that registered after a
    ///     reconnect would be granted — and would refuse — the very next lease.
    /// </summary>
    [Fact]
    public async Task ADrainingMember_DoesNotRegisterAfterAReconnect()
    {
        var member = CreateMember();
        await member.Service.OnConnectedAsync(false);
        await member.Client.DrainAsync("its lease expired");
        member.Hub.Calls.Clear();

        await member.Service.OnConnectedAsync(true);

        Assert.DoesNotContain("register", member.Hub.Calls);
        Assert.False(member.State.IsRegistered);
        Assert.Contains("draining", member.State.LastFailureMessage);
    }

    [Fact]
    public async Task ADrainingMember_StillHeartbeatsWhileItIsRegistered()
    {
        // Registered on the connection the drain was reported on: the controller already knows it
        // drains, and the heartbeat keeps that registration (and its drain flag) alive until exit.
        var member = CreateMember();
        await member.Service.OnConnectedAsync(false);
        await member.Client.DrainAsync("pool idle");
        member.Hub.Calls.Clear();

        await member.Service.TickAsync();

        Assert.Equal(["heartbeat"], member.Hub.Calls);
    }

    // ---------------------------------------------------------------------------------------------
    // Readiness
    // ---------------------------------------------------------------------------------------------

    private static Task<HealthCheckResult> CheckReadinessAsync(Member member, bool probeEnabled = true)
    {
        var hub = A.Fake<IAdapterPoolHubClient>();
        A.CallTo(() => hub.IsAlive).Returns(true);
        var options = Options.Create(new AdapterOptions
        {
            HubReadinessProbeEnabled = probeEnabled, HubReadinessGracePeriod = TimeSpan.Zero
        });
        return new AdapterPoolHubReadinessHealthCheck(hub, member.State, options, member.Client)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    [Fact]
    public async Task Readiness_ARegisteredMemberThatDrains_IsNotReady_AndSaysWhy()
    {
        var member = CreateMember();
        await member.Service.OnConnectedAsync(false);
        Assert.Equal(HealthStatus.Healthy, (await CheckReadinessAsync(member)).Status);

        await member.Client.DrainAsync("its lease expired");
        var result = await CheckReadinessAsync(member);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("draining", result.Description);
        Assert.Contains("its lease expired", result.Description);
    }

    [Fact]
    public async Task Readiness_TheProbeSwitch_StillWins()
    {
        var member = CreateMember();
        await member.Client.DrainAsync("its lease expired");

        var result = await CheckReadinessAsync(member, probeEnabled: false);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // ---------------------------------------------------------------------------------------------
    // Exit once drained and idle
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Exit_AMemberThatIsNotDraining_IsNeverStopped()
    {
        var member = CreateMember();
        var (exit, lifetime) = CreateExit(member);

        Assert.False(exit.Evaluate());
        A.CallTo(() => lifetime.StopApplication()).MustNotHaveHappened();
    }

    [Fact]
    public async Task Exit_ADrainingMemberHoldingALease_IsNotStoppedUntilTheLeaseIsReleased()
    {
        var work = new WorkItem(LeaseWorkOutcome.Succeeded()) { Hold = new TaskCompletionSource() };
        var member = CreateMember(work);
        var (exit, lifetime) = CreateExit(member);
        var lease = member.Client.LeaseAsync(ALease());
        await work.Entered.Task;
        var drain = member.Client.DrainAsync("pool idle");

        Assert.False(exit.Evaluate());
        A.CallTo(() => lifetime.StopApplication()).MustNotHaveHappened();

        work.Hold.SetResult();
        await lease;
        await drain;

        Assert.True(exit.Evaluate());
        A.CallTo(() => lifetime.StopApplication()).MustHaveHappenedOnceExactly();
    }

    /// <summary>The repro end to end: a failed leave, then the process stops on its own.</summary>
    [Fact]
    public async Task Exit_AMemberThatDrainedItself_StopsTheApplication()
    {
        var member = CreateMember(new WorkItem(LeaseWorkOutcome.Succeeded()), [new FailingLeaveParticipant()]);
        var (exit, lifetime) = CreateExit(member);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => lifetime.StopApplication()).Invokes(() => stopped.TrySetResult());
        exit.CheckInterval = TimeSpan.FromMilliseconds(10);

        await member.Client.LeaseAsync(ALease());
        await exit.StartAsync(CancellationToken.None);
        var finished = await Task.WhenAny(stopped.Task,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await exit.StopAsync(CancellationToken.None);

        Assert.Same(stopped.Task, finished);
    }

    [Fact]
    public async Task Exit_DisabledByConfiguration_NeverStopsTheApplication()
    {
        var member = CreateMember(exitWhenDrained: false);
        var (exit, lifetime) = CreateExit(member);
        exit.CheckInterval = TimeSpan.FromMilliseconds(10);
        await member.Client.DrainAsync("its lease expired");

        await exit.StartAsync(CancellationToken.None);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await exit.StopAsync(CancellationToken.None);

        A.CallTo(() => lifetime.StopApplication()).MustNotHaveHappened();
    }

    [Fact]
    public void Options_ExitWhenDrained_DefaultsToTrue()
    {
        Assert.True(new AdapterPoolMemberOptions().ExitWhenDrained);
    }
}
