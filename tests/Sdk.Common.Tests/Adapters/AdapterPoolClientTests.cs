using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#4924 increment 6 — the member's half of the lease protocol.
/// </summary>
/// <remarks>
///     🔴 The ordering assertions are the substance here. Concept §4 says the process must retain
///     nothing tenant-scoped between leases; what makes that true in practice is that the tenant is
///     gone from the process <b>before</b> the controller is told the member is free — because the
///     controller's very next act is to hand it another tenant.
/// </remarks>
public class AdapterPoolClientTests
{
    private const string Secret = "sJ8k2p-QmZ4x7vNb1LcT0aRwEyUiOpAsDfGhJkLzXcVbNm";

    private static LeaseDto ALease(string tenantId = "tenant-a", string leaseId = "lease-1") => new()
    {
        LeaseId = leaseId,
        TenantId = tenantId,
        AdapterPoolTenantId = "lender",
        AdapterPoolRtId = "665f0000000000000000ee21",
        AdapterRtId = "665f0000000000000000ee22",
        AdapterCkTypeId = "System.Communication/Adapter",
        ClientId = "octo-pipeline-sa-borrower",
        ClientSecret = Secret,
        GrantedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
    };

    /// <summary>Records what happened, in the order it happened.</summary>
    private sealed class Journal
    {
        private readonly List<string> _entries = [];
        private readonly Lock _gate = new();

        public void Add(string entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
            }
        }

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToList();
                }
            }
        }
    }

    private sealed class JournalParticipant(Journal journal, string name, bool throwOnEnter = false,
        bool throwOnLeave = false) : IAdapterLeaseParticipant
    {
        public Task EnterLeaseAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            journal.Add($"enter:{name}");
            return throwOnEnter
                ? Task.FromException(new InvalidOperationException($"{name} refused to enter"))
                : Task.CompletedTask;
        }

        public Task LeaveLeaseAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            journal.Add($"leave:{name}");
            return throwOnLeave
                ? Task.FromException(new InvalidOperationException($"{name} refused to leave"))
                : Task.CompletedTask;
        }
    }

    private sealed class JournalWorkItem(Journal journal, IAdapterLeaseScope scope,
        bool succeed = true, Func<Task>? body = null) : IAdapterLeaseWorkItem
    {
        public async Task<LeaseWorkOutcome> RunAsync(LeaseDto lease, CancellationToken cancellationToken)
        {
            // The tenant MUST be in scope here — that is the point of the whole sequence.
            journal.Add($"work:{scope.TenantId}");
            if (body is not null)
            {
                await body();
            }

            return succeed ? LeaseWorkOutcome.Succeeded("done") : LeaseWorkOutcome.Failed("the work failed");
        }
    }

    private sealed class RecordingHubClient : IAdapterPoolHubClient
    {
        private readonly Journal _journal;
        private readonly IAdapterLeaseScope _scope;
        private readonly Func<Exception>? _throwFactory;

        public RecordingHubClient(Journal journal, IAdapterLeaseScope scope, Func<Exception>? throwFactory = null)
        {
            _journal = journal;
            _scope = scope;
            _throwFactory = throwFactory;
        }

        public List<LeaseResultDto> Releases { get; } = [];
        public int RegistrationAttempts { get; private set; }

        /// <summary>AB#4924: what the member actually put on the wire.</summary>
        public PoolMemberRegistrationDto? LastRegistration { get; private set; }

        public Task<PoolMemberRegistrationResultDto> RegisterPoolMemberAsync(PoolMemberRegistrationDto registration)
        {
            RegistrationAttempts++;
            LastRegistration = registration;
            if (_throwFactory is not null)
            {
                return Task.FromException<PoolMemberRegistrationResultDto>(_throwFactory());
            }

            return Task.FromResult(new PoolMemberRegistrationResultDto
            {
                Accepted = true,
                MemberId = registration.MemberId,
                HeartbeatIntervalSeconds = 30
            });
        }

        public Task ReleaseLeaseAsync(LeaseResultDto result)
        {
            // 🔴 The assertion that matters: by the time the controller hears about the release, the
            // process must already be tenant-free. Recorded rather than asserted so the failure shows
            // up in the journal with the rest of the sequence.
            _journal.Add(_scope.HasLease ? "release:STILL-LEASED" : "release:tenant-free");
            Releases.Add(result);
            return _throwFactory is not null ? Task.FromException(_throwFactory()) : Task.CompletedTask;
        }

        public Task HeartbeatAsync(PoolMemberHeartbeatDto heartbeat)
        {
            return _throwFactory is not null ? Task.FromException(_throwFactory()) : Task.CompletedTask;
        }

        // Transport surface of ISignalRClient — not what this suite is about; the lease protocol is.
        public IServiceClientAccessToken ClientAccessToken { get; } = new ServiceClientAccessToken();
        public AdapterPoolHubClientOptions Options { get; } = new();
        public Uri? ServiceUri => new("https://controller.example.com/adapterPoolHub");
        public bool IsAlive => true;
        public void EnableReconnect(Func<bool, Task> onReconnectFunction) { }
        public Task StartAsync(Func<bool, Task> onConnectFunction, CancellationToken stoppingToken) =>
            Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    private static AdapterPoolClient CreateClient(IAdapterLeaseScope scope, IAdapterPoolHubClient hubClient,
        IEnumerable<IAdapterLeaseParticipant> participants, IAdapterLeaseWorkItem workItem,
        INodeSchemaRegistry? nodeSchemaRegistry = null, IPipelineSchemaGenerator? pipelineSchemaGenerator = null)
    {
        return new AdapterPoolClient(scope, hubClient, participants, workItem,
            new OptionsWrapper<AdapterPoolMemberOptions>(new AdapterPoolMemberOptions
            {
                AdapterPoolTenantId = "lender",
                AdapterPoolRtId = "665f0000000000000000ee21",
                MemberId = "octo-pool-0"
            }),
            NullLogger<AdapterPoolClient>.Instance, nodeSchemaRegistry, pipelineSchemaGenerator);
    }

    private const string PoolSchemaJson = "{\"$id\":\"pool-schema\"}";

    private sealed class StubNodeSchemaRegistry(params NodeDescriptor[] descriptors) : INodeSchemaRegistry
    {
        public IReadOnlyList<NodeDescriptor> GetAllDescriptors() => descriptors;

        public NodeDescriptor? GetDescriptor(string qualifiedName) =>
            descriptors.FirstOrDefault(d => $"{d.NodeName}@{d.Version}" == qualifiedName);
    }

    private sealed class StubSchemaGenerator(string schema) : IPipelineSchemaGenerator
    {
        public string GenerateSchema() => schema;
    }

    private sealed class ThrowingNodeSchemaRegistry : INodeSchemaRegistry
    {
        public IReadOnlyList<NodeDescriptor> GetAllDescriptors() =>
            throw new InvalidOperationException("the registry is broken");

        public NodeDescriptor? GetDescriptor(string qualifiedName) => null;
    }

    /// <summary>
    ///     🔴 AB#4924 — the registration carries the member's node descriptors and pipeline schema.
    /// </summary>
    /// <remarks>
    ///     Without them the controller has nothing to answer "which nodes can this pool run" with, and
    ///     a BORROWER's DeployPipeline falls through to the name-based fallback: every leased pipeline
    ///     keeps the CK default execution class, and its definition is validated against no schema.
    ///     The old <c>NodeNames</c> field was hard-coded to an empty list here, which is why the gap
    ///     was invisible.
    /// </remarks>
    [Fact]
    public async Task Registration_CarriesTheMembersNodeDescriptorsAndPipelineSchema()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient, [], new JournalWorkItem(journal, scope),
            new StubNodeSchemaRegistry(
                new NodeDescriptor("FromCustomThing", 1, "Trigger", true, false, "{}",
                    ExecutionClass: PipelineExecutionClass.Interactive),
                new NodeDescriptor("FromPolling", 1, "Trigger", true, false, "{}",
                    RequiresRunningProcess: true)),
            new StubSchemaGenerator(PoolSchemaJson));

        await client.RegisterAsync();

        var registration = hubClient.LastRegistration;
        Assert.NotNull(registration);
        Assert.Equal(2, registration!.NodeDescriptors.Count);
        var interactive = Assert.Single(registration.NodeDescriptors, d => d.NodeName == "FromCustomThing");
        // The two fields the controller's deploy path actually reads.
        Assert.Equal((int)PipelineExecutionClass.Interactive, interactive.ExecutionClass);
        Assert.True(Assert.Single(registration.NodeDescriptors, d => d.NodeName == "FromPolling")
            .RequiresRunningProcess);
        Assert.Equal(PoolSchemaJson, registration.PipelineSchemaJson);
    }

    /// <summary>
    ///     A member that cannot describe itself still registers and is still leasable — the same
    ///     degradation the dedicated path already makes.
    /// </summary>
    [Fact]
    public async Task Registration_WithoutAUsableRegistry_StillSucceedsAndReportsNoDescriptors()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient, [], new JournalWorkItem(journal, scope),
            new ThrowingNodeSchemaRegistry());

        var result = await client.RegisterAsync();

        Assert.NotNull(result);
        Assert.True(result!.Accepted);
        Assert.NotNull(hubClient.LastRegistration);
        Assert.Empty(hubClient.LastRegistration!.NodeDescriptors);
        Assert.Null(hubClient.LastRegistration.PipelineSchemaJson);
    }

    /// <summary>
    ///     🔴 The full sequence, in order. Participants enter in registration order, the work item runs
    ///     with the tenant in scope, participants leave in <b>reverse</b> order, and only then — with
    ///     the process already tenant-free — is the release reported.
    /// </summary>
    [Fact]
    public async Task ALease_EntersRunsAndLeavesInOrder_AndReportsOnlyOnceTenantFree()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope,
            hubClient,
            [new JournalParticipant(journal, "token"), new JournalParticipant(journal, "ckCache")],
            new JournalWorkItem(journal, scope));

        await client.LeaseAsync(ALease());

        Assert.Equal(
            ["enter:token", "enter:ckCache", "work:tenant-a", "leave:ckCache", "leave:token",
                "release:tenant-free"],
            journal.Entries);
        Assert.False(scope.HasLease);
        Assert.Single(hubClient.Releases);
        Assert.Equal(LeaseReleaseReasonDto.Completed, hubClient.Releases[0].Reason);
        Assert.True(hubClient.Releases[0].Success);
    }

    /// <summary>
    ///     A failing work item is still a clean release: the member unwound the lease itself, so the
    ///     failure belongs to the pipeline and not to the isolation invariant.
    /// </summary>
    [Fact]
    public async Task AFailingWorkItem_StillLeavesEveryParticipantAndReleasesTheTenant()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient,
            [new JournalParticipant(journal, "token")],
            new JournalWorkItem(journal, scope, succeed: false));

        await client.LeaseAsync(ALease());

        Assert.Equal(["enter:token", "work:tenant-a", "leave:token", "release:tenant-free"], journal.Entries);
        Assert.False(scope.HasLease);
        Assert.Equal(LeaseReleaseReasonDto.Failed, hubClient.Releases[0].Reason);
        Assert.False(hubClient.Releases[0].Success);
    }

    /// <summary>
    ///     🔴 A participant whose enter threw has nothing to leave; the ones that did enter still must.
    ///     Tearing down a half-constructed state in a way nobody designed is how a tenant survives a
    ///     release.
    /// </summary>
    [Fact]
    public async Task AParticipantThatFailsToEnter_UnwindsOnlyTheOnesThatDid()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient,
            [
                new JournalParticipant(journal, "token"),
                new JournalParticipant(journal, "ckCache", throwOnEnter: true),
                new JournalParticipant(journal, "registration")
            ],
            new JournalWorkItem(journal, scope));

        await client.LeaseAsync(ALease());

        // "registration" never entered, so it never leaves. "ckCache" threw on enter, so it has
        // nothing to leave either. "token" entered and must be unwound.
        Assert.Equal(["enter:token", "enter:ckCache", "leave:token", "release:tenant-free"], journal.Entries);
        Assert.False(scope.HasLease);
        Assert.False(hubClient.Releases[0].Success);
    }

    /// <summary>
    ///     🔴 Concept §6: a member whose post-lease cleanliness is unproven is drained and restarted
    ///     rather than re-used. A leave that threw is exactly that case — and the answer is not a
    ///     logged shrug, it is that the member never serves a second tenant.
    /// </summary>
    [Fact]
    public async Task AParticipantThatFailsToLeave_DrainsTheMember()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient,
            [new JournalParticipant(journal, "ckCache", throwOnLeave: true)],
            new JournalWorkItem(journal, scope));

        await client.LeaseAsync(ALease());

        Assert.True(client.IsDraining);
        Assert.Equal(LeaseReleaseReasonDto.Drained, hubClient.Releases[0].Reason);
        Assert.False(hubClient.Releases[0].Success);
        // The lease scope itself is still released: the member holds no tenant even though one of its
        // participants could not prove it dropped its own state.
        Assert.False(scope.HasLease);
    }

    /// <summary>
    ///     🔴 Two overlapping leases on one process is the cross-tenant incident the design exists to
    ///     make impossible. The controller's registry already prevents it, so reaching here means the
    ///     two views diverged — and the safe answer is never "serve them both".
    /// </summary>
    [Fact]
    public async Task ASecondConcurrentLease_IsRefusedRatherThanApplied()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);

        var secondLeaseArrived = new TaskCompletionSource();
        var firstMayFinish = new TaskCompletionSource();

        AdapterPoolClient client = null!;
        client = CreateClient(scope, hubClient,
            [],
            new JournalWorkItem(journal, scope, body: async () =>
            {
                // The second lease arrives while the first work item is still running.
                await client.LeaseAsync(ALease("tenant-b", "lease-2"));
                secondLeaseArrived.SetResult();
                await firstMayFinish.Task;
            }));

        var first = client.LeaseAsync(ALease());
        await secondLeaseArrived.Task;
        firstMayFinish.SetResult();
        await first;

        // Tenant B never entered the process.
        Assert.DoesNotContain("work:tenant-b", journal.Entries);
        var refusal = hubClient.Releases.Single(r => r.LeaseId == "lease-2");
        Assert.False(refusal.Success);
        Assert.Equal(LeaseReleaseReasonDto.Failed, refusal.Reason);
    }

    [Fact]
    public async Task ALeaseArrivingAfterADrain_IsHandedStraightBack()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient, [], new JournalWorkItem(journal, scope));

        await client.DrainAsync("pool scaled in");
        await client.LeaseAsync(ALease());

        Assert.DoesNotContain("work:tenant-a", journal.Entries);
        Assert.False(scope.HasLease);
        Assert.Equal(LeaseReleaseReasonDto.Drained, hubClient.Releases[0].Reason);
    }

    /// <summary>
    ///     Skew rule, as for AB#4917: a controller that pre-dates this contract rejects the hub method
    ///     with a <c>HubException</c>. The member degrades — it logs once and keeps running — rather
    ///     than crash-looping through its reconnect loop.
    /// </summary>
    [Fact]
    public async Task AControllerThatDoesNotKnowTheContract_DegradesRatherThanThrows()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope,
            () => new HubException("Method does not exist."));
        var client = CreateClient(scope, hubClient, [], new JournalWorkItem(journal, scope));

        var registration = await client.RegisterAsync();
        await client.HeartbeatAsync();

        Assert.Null(registration);
        Assert.Equal(1, hubClient.RegistrationAttempts);
    }

    /// <summary>
    ///     🔴 A release the controller never hears must still leave the member tenant-free. The lease
    ///     expires on the controller's TTL; what must never happen is a member that keeps the tenant
    ///     because it could not report that it dropped it.
    /// </summary>
    [Fact]
    public async Task AFailedReleaseReport_StillLeavesTheMemberTenantFree()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope,
            () => new HubException("Method does not exist."));
        var client = CreateClient(scope, hubClient,
            [new JournalParticipant(journal, "token")],
            new JournalWorkItem(journal, scope));

        await client.LeaseAsync(ALease());

        Assert.False(scope.HasLease);
        Assert.Contains("leave:token", journal.Entries);
    }

    /// <summary>
    ///     AB#4924 increment 9 (plan §11). The member is the only party that can time the work item:
    ///     for a leased execution the controller stamps <c>StartedAt</c> at claim time, so its own
    ///     view of "how long did the pipeline run" is identical to "how long was the member held" by
    ///     construction, and the per-lease warm-up concept §2.3 exists to expose would read as zero
    ///     forever. The measurement travels back on the release.
    /// </summary>
    [Fact]
    public async Task TheRelease_CarriesTheTimeTheWorkItemActuallyRan()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope,
            hubClient,
            [new JournalParticipant(journal, "token")],
            new JournalWorkItem(journal, scope, body: () => Task.Delay(30)));

        await client.LeaseAsync(ALease());

        var release = Assert.Single(hubClient.Releases);
        Assert.NotNull(release.WorkDurationMs);
        // At least the delay the work item took, and nowhere near the whole lease — participants and
        // the release round trip are OUTSIDE this span on purpose, because they are the overhead.
        Assert.InRange(release.WorkDurationMs!.Value, 20, 10_000);
    }

    /// <summary>
    ///     A lease the member refused — it is draining — never ran a work item, so it reports no work
    ///     span at all. A zero here would tell the controller the member spent 100 % of the lease on
    ///     overhead, which is the most alarming possible reading of "nothing happened".
    /// </summary>
    [Fact]
    public async Task ALeaseRefusedBeforeTheWorkItemRan_ReportsNoWorkSpanAtAll()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope, hubClient, [], new JournalWorkItem(journal, scope));

        await client.DrainAsync("the operator asked");
        await client.LeaseAsync(ALease());

        var release = Assert.Single(hubClient.Releases);
        Assert.Equal(LeaseReleaseReasonDto.Drained, release.Reason);
        Assert.Null(release.WorkDurationMs);
    }

    /// <summary>
    ///     A work item that threw still held the tenant for as long as it ran. Dropping that sample
    ///     would bias the overhead distribution towards the leases that succeeded.
    /// </summary>
    [Fact]
    public async Task AWorkItemThatThrew_StillReportsHowLongItRan()
    {
        var journal = new Journal();
        var scope = new AdapterPoolTenantScope();
        var hubClient = new RecordingHubClient(journal, scope);
        var client = CreateClient(scope,
            hubClient,
            [],
            new JournalWorkItem(journal, scope, body: async () =>
            {
                await Task.Delay(30);
                throw new InvalidOperationException("the pipeline blew up");
            }));

        await client.LeaseAsync(ALease());

        var release = Assert.Single(hubClient.Releases);
        Assert.False(release.Success);
        Assert.NotNull(release.WorkDurationMs);
        Assert.InRange(release.WorkDurationMs!.Value, 20, 10_000);
    }
}
