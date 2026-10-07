using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5826 — the member half of "a lease survives a controller restart": it registers WITH its
///     running lease so the controller takes it over, and it keeps a release it could not report
///     instead of dropping it.
/// </summary>
/// <remarks>
///     Reproduced on test-2-dev (2026-10-07): the member finished its lease two minutes after a
///     controller restart, its release went into the void, and the execution was failed 31 minutes
///     later. Before that, a release attempted while the connection was down was dropped outright
///     ("the controller will expire it on TTL").
/// </remarks>
public class AdapterPoolMemberLeaseResumptionTests
{
    private const string LeasedTenant = "borrower-a";
    private const string MemberId = "octo-pool-0";

    private static AdapterPoolMemberOptions ConfiguredPool() => new()
    {
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        MemberId = MemberId
    };

    private static LeaseDto ALease(string leaseId = "lease-7") => new()
    {
        LeaseId = leaseId,
        TenantId = LeasedTenant,
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        AdapterRtId = "665f0000000000000000d101",
        AdapterCkTypeId = "System.Communication/MeshAdapter",
        ExecutionId = "exec-7",
        ClientId = "octo-pipeline-sa",
        ClientSecret = "the-borrowers-secret",
        DatabasePassword = "the-borrowers-db-password",
        GrantedAtUtc = new DateTime(2026, 10, 7, 19, 28, 13, DateTimeKind.Utc),
        ExpiresAtUtc = new DateTime(2026, 10, 7, 19, 43, 13, DateTimeKind.Utc)
    };

    /// <summary>A pool hub that supports resumption, with scriptable answers and failures.</summary>
    private sealed class ResumingHubClient : IAdapterPoolHubClient
    {
        public List<string> Calls { get; } = [];
        public List<LeaseResultDto> Releases { get; } = [];
        public List<PoolMemberRegistrationDto> Resumptions { get; } = [];

        public Func<PoolMemberRegistrationDto, Task<PoolMemberRegistrationResultDto>> OnResume { get; set; } =
            r => Task.FromResult(new PoolMemberRegistrationResultDto
            {
                Accepted = true, MemberId = r.MemberId, HeartbeatIntervalSeconds = 30, ActiveLeaseAdopted = true
            });

        public Func<Exception?> ReleaseFailure { get; set; } = () => null;

        public Task<PoolMemberRegistrationResultDto> RegisterPoolMemberAsync(PoolMemberRegistrationDto registration)
        {
            Calls.Add("register");
            return Task.FromResult(new PoolMemberRegistrationResultDto
            {
                Accepted = true, MemberId = registration.MemberId, HeartbeatIntervalSeconds = 30
            });
        }

        public Task<PoolMemberRegistrationResultDto> ResumePoolMemberAsync(PoolMemberRegistrationDto registration)
        {
            Calls.Add("resume");
            Resumptions.Add(registration);
            return OnResume(registration);
        }

        public Task ReleaseLeaseAsync(LeaseResultDto result)
        {
            var failure = ReleaseFailure();
            if (failure is not null)
            {
                Calls.Add("release-failed");
                return Task.FromException(failure);
            }

            Calls.Add("release");
            Releases.Add(result);
            return Task.CompletedTask;
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

    private sealed class BlockingWorkItem : IAdapterLeaseWorkItem
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            return LeaseWorkOutcome.Succeeded(outputData: "{\"booked\":42}");
        }
    }

    /// <summary>Signals when the member starts leaving the lease, i.e. right before it reports the release.</summary>
    private sealed class LeavingSignal : IAdapterLeaseParticipant
    {
        public TaskCompletionSource Leaving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnterLeaseAsync(LeaseDto lease, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LeaveLeaseAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            Leaving.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed record Member(
        ResumingHubClient Hub,
        AdapterPoolClient Client,
        AdapterPoolMemberService Service,
        IAdapterHubRegistrationState State);

    private static Member CreateMember(IAdapterLeaseWorkItem? workItem = null,
        IReadOnlyList<IAdapterLeaseParticipant>? participants = null)
    {
        var hub = new ResumingHubClient();
        var state = new AdapterHubRegistrationState();
        var options = new OptionsWrapper<AdapterPoolMemberOptions>(ConfiguredPool());
        var client = new AdapterPoolClient(new AdapterPoolTenantScope(), hub, participants ?? [],
            workItem ?? new NoAdapterLeaseWorkItem(), options, NullLogger<AdapterPoolClient>.Instance,
            registrationState: state);
        var service = new AdapterPoolMemberService(client, hub, options,
            NullLogger<AdapterPoolMemberService>.Instance);
        return new Member(hub, client, service, state);
    }

    private static async Task<(Member Member, BlockingWorkItem WorkItem, Task Lease)> MemberRunningALeaseAsync()
    {
        var workItem = new BlockingWorkItem();
        var member = CreateMember(workItem);
        await member.Service.OnConnectedAsync(false);
        var lease = member.Client.LeaseAsync(ALease());
        await workItem.Entered.Task;
        member.Hub.Calls.Clear();
        return (member, workItem, lease);
    }

    // ---------------------------------------------------------------------------------------------
    // The release names the execution
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     🔴 A controller that no longer holds the lease can only attribute the outcome if the release
    ///     names the execution, its tenant and the member.
    /// </summary>
    [Fact]
    public async Task TheRelease_NamesTheExecutionTheTenantAndTheMember()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();

        workItem.Release.SetResult();
        await lease;

        var release = Assert.Single(member.Hub.Releases);
        Assert.Equal("exec-7", release.ExecutionId);
        Assert.Equal(LeasedTenant, release.TenantId);
        Assert.Equal(MemberId, release.MemberId);
        Assert.Equal("{\"booked\":42}", release.OutputData);
    }

    // ---------------------------------------------------------------------------------------------
    // Reconnect during a lease: resume instead of defer
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     🔴 The F1 path. The member registers again at once, naming the running lease, and is
    ///     registered — its readiness is green during the lease, and the heartbeat tick does not try
    ///     to register a second time.
    /// </summary>
    [Fact]
    public async Task AReconnectDuringALease_ResumesTheLeaseAndIsRegistered()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();

        await member.Service.OnConnectedAsync(true);
        await member.Service.TickAsync();

        var resumption = Assert.Single(member.Hub.Resumptions).ActiveLease;
        Assert.NotNull(resumption);
        Assert.Equal("lease-7", resumption!.LeaseId);
        Assert.Equal("exec-7", resumption.ExecutionId);
        Assert.Equal(LeasedTenant, resumption.TenantId);
        Assert.Equal(ALease().ExpiresAtUtc, resumption.ExpiresAtUtc);
        Assert.True(member.State.IsRegistered);
        Assert.Equal(["resume", "heartbeat"], member.Hub.Calls);

        workItem.Release.SetResult();
        await lease;
        Assert.Equal("release", member.Hub.Calls[^1]);
    }

    /// <summary>
    ///     The lease travels back as identifiers only — no secret, no payload. The member keeps those,
    ///     and the controller does not need them to know the member is busy.
    /// </summary>
    [Fact]
    public async Task TheResumption_CarriesNoSecretOfTheBorrower()
    {
        var (member, _, _) = await MemberRunningALeaseAsync();

        await member.Service.OnConnectedAsync(true);

        var json = System.Text.Json.JsonSerializer.Serialize(Assert.Single(member.Hub.Resumptions));
        Assert.DoesNotContain("the-borrowers-secret", json);
        Assert.DoesNotContain("the-borrowers-db-password", json);
    }

    /// <summary>
    ///     A controller that could not adopt the lease (the execution moved on) still registers the
    ///     member as busy. The member is registered, runs the work item to its end, and reports it.
    /// </summary>
    [Fact]
    public async Task ALeaseTheControllerDidNotAdopt_StillLeavesTheMemberRegisteredAndReporting()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();
        member.Hub.OnResume = r => Task.FromResult(new PoolMemberRegistrationResultDto
        {
            Accepted = true, MemberId = r.MemberId, ActiveLeaseAdopted = false,
            StatusMessage = "execution 'exec-7' is Interrupted already"
        });

        await member.Service.OnConnectedAsync(true);
        workItem.Release.SetResult();
        await lease;

        Assert.True(member.State.IsRegistered);
        Assert.Equal(["resume", "release"], member.Hub.Calls);
    }

    /// <summary>
    ///     A controller that pre-dates AB#5826 answers "unknown hub method". The member falls back to
    ///     deferring, latches that for the connection (the tick does not ask again), and asks again on
    ///     the next connection — the controller pod behind it may be newer.
    /// </summary>
    [Fact]
    public async Task AControllerWithoutResumption_IsAskedOncePerConnection()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();
        member.Hub.OnResume = _ => Task.FromException<PoolMemberRegistrationResultDto>(
            new HubException("Unknown hub method 'ResumePoolMemberAsync'"));

        await member.Service.OnConnectedAsync(true);
        await member.Service.TickAsync();

        Assert.True(member.Client.IsResumptionUnsupported);
        Assert.False(member.State.IsRegistered);
        Assert.Equal(["resume"], member.Hub.Calls);

        await member.Service.OnConnectedAsync(true);
        Assert.Equal(["resume", "resume"], member.Hub.Calls);

        workItem.Release.SetResult();
        await lease;
        await member.Service.TickAsync();
        Assert.Equal("register", member.Hub.Calls[^1]);
    }

    /// <summary>A transport failure is not "unsupported": it is rethrown for the reconnect loop.</summary>
    [Fact]
    public async Task ATransportFailureDuringTheResumption_IsRethrownAndNotLatched()
    {
        var (member, _, _) = await MemberRunningALeaseAsync();
        member.Hub.OnResume = _ => Task.FromException<PoolMemberRegistrationResultDto>(
            new InvalidOperationException("InvokeCoreAsync cannot be called if the connection is not active"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => member.Service.OnConnectedAsync(true));

        Assert.False(member.Client.IsResumptionUnsupported);
        Assert.False(member.State.IsRegistered);
    }

    /// <summary>
    ///     A member whose registration was lost while a lease runs (a rejected heartbeat) resumes from
    ///     the tick rather than waiting for the lease to end.
    /// </summary>
    [Fact]
    public async Task TheTick_ResumesARunningLeaseWhenTheRegistrationWasLost()
    {
        var (member, _, _) = await MemberRunningALeaseAsync();
        member.State.MarkNotRegistered("the controller rejected a heartbeat");

        await member.Service.TickAsync();

        Assert.Equal(["resume"], member.Hub.Calls);
        Assert.True(member.State.IsRegistered);
    }

    /// <summary>
    ///     🔴 The resumption must reach the controller before the release of the same lease. A release
    ///     that overtook it would complete the execution, and the resumption would then record the
    ///     member busy with a lease that is over — a member that takes no work until the TTL reaper
    ///     drains it. The release of a lease that ends while the resumption is in flight waits for it.
    /// </summary>
    [Fact]
    public async Task AReleaseNeverOvertakesTheResumptionOfTheSameLease()
    {
        var workItem = new BlockingWorkItem();
        var leaving = new LeavingSignal();
        var member = CreateMember(workItem, [leaving]);
        await member.Service.OnConnectedAsync(false);
        var lease = member.Client.LeaseAsync(ALease());
        await workItem.Entered.Task;
        member.Hub.Calls.Clear();

        var resumeCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeAnswer = new TaskCompletionSource<PoolMemberRegistrationResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        member.Hub.OnResume = _ =>
        {
            resumeCalled.TrySetResult();
            return resumeAnswer.Task;
        };

        var connect = member.Service.OnConnectedAsync(true);
        await resumeCalled.Task;

        workItem.Release.SetResult();
        await leaving.Leaving.Task;
        // Room for a release that is not held back to overtake; it must not.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("release", member.Hub.Calls);

        resumeAnswer.SetResult(new PoolMemberRegistrationResultDto
        {
            Accepted = true, MemberId = MemberId, HeartbeatIntervalSeconds = 30, ActiveLeaseAdopted = true
        });
        await connect;
        await lease;

        Assert.Equal(["resume", "release"], member.Hub.Calls);
    }

    // ---------------------------------------------------------------------------------------------
    // A release that could not be reported is kept and re-sent — before the registration
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     🔴 The push-train finding (execution 56ecf2b8): the member finished while the controller was
    ///     down, the release failed, and was dropped. It is now kept and sent on the next connect —
    ///     before the member registers, so the controller learns the outcome before it learns the
    ///     member is idle.
    /// </summary>
    [Fact]
    public async Task AReleaseThatFailedWhileDisconnected_IsSentOnTheNextConnect_BeforeTheRegistration()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();
        member.Hub.ReleaseFailure = () => new InvalidOperationException("the connection is not active");

        workItem.Release.SetResult();
        await lease;
        Assert.Equal(1, member.Client.PendingReleaseCount);

        member.Hub.ReleaseFailure = () => null;
        await member.Service.OnConnectedAsync(true);

        Assert.Equal(["release-failed", "release", "register"], member.Hub.Calls);
        var release = Assert.Single(member.Hub.Releases);
        Assert.Equal("exec-7", release.ExecutionId);
        Assert.Equal(0, member.Client.PendingReleaseCount);
    }

    [Fact]
    public async Task APendingReleaseThatFailsAgain_IsKeptAndRetriedByTheTick()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();
        member.Hub.ReleaseFailure = () => new InvalidOperationException("the connection is not active");
        workItem.Release.SetResult();
        await lease;

        await member.Client.FlushPendingReleasesAsync();
        Assert.Equal(1, member.Client.PendingReleaseCount);

        member.Hub.ReleaseFailure = () => null;
        await member.Service.TickAsync();

        Assert.Equal(0, member.Client.PendingReleaseCount);
        Assert.Single(member.Hub.Releases);
    }

    /// <summary>
    ///     A draining member exits once it is idle — but not while an outcome is still undelivered,
    ///     or the result dies with the process.
    /// </summary>
    [Fact]
    public async Task ADrainingMember_IsNotIdleWhileAReleaseIsPending()
    {
        var (member, workItem, lease) = await MemberRunningALeaseAsync();
        // The drain flag is set at once; the call itself waits for the running lease to finish.
        var drain = member.Client.DrainAsync("pool shrinks");
        member.Hub.ReleaseFailure = () => new InvalidOperationException("the connection is not active");
        workItem.Release.SetResult();
        await lease;
        await drain;

        Assert.False(member.Client.IsDrainedAndIdle);

        member.Hub.ReleaseFailure = () => null;
        await member.Client.FlushPendingReleasesAsync();

        Assert.True(member.Client.IsDrainedAndIdle);
    }
}
