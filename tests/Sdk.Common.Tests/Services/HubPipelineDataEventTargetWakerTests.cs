using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sdk.Common.Tests.Services;

/// <summary>
///     AB#5231: the adapter-side wake gate for pipeline data events. In-process targets cost nothing,
///     remote targets ask the controller, a failed ask never stops the publish.
/// </summary>
public class HubPipelineDataEventTargetWakerTests
{
    private const string TenantId = "acme";
    private readonly IAdapterHubClient _hub = A.Fake<IAdapterHubClient>();
    private readonly IPipelineRegistryService _registry = A.Fake<IPipelineRegistryService>();
    private readonly RtEntityId _target = new(new RtCkId<CkTypeId>("System.Communication/Pipeline"), OctoObjectId.GenerateNewId());
    private readonly HubPipelineDataEventTargetWaker _waker;

    public HubPipelineDataEventTargetWakerTests()
    {
        _waker = new HubPipelineDataEventTargetWaker(_hub, _registry, NullLogger<HubPipelineDataEventTargetWaker>.Instance);
    }

    [Fact]
    public async Task ATargetInThisProcess_IsNotAskedAbout()
    {
        PipelineRegistration? ignored = null;
        A.CallTo(() => _registry.TryGetPipelineRegistration(TenantId, _target, out ignored)).Returns(true);

        await _waker.EnsureTargetRunningAsync(TenantId, _target, TestContext.Current.CancellationToken);

        // The sender is running, so the target is too — the common in-workload chaining case.
        A.CallTo(() => _hub.EnsurePipelineWorkloadRunningAsync(A<RtEntityId>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ATargetOnAnotherWorkload_IsWokenThroughTheController()
    {
        PipelineRegistration? ignored = null;
        A.CallTo(() => _registry.TryGetPipelineRegistration(TenantId, _target, out ignored)).Returns(false);

        await _waker.EnsureTargetRunningAsync(TenantId, _target, TestContext.Current.CancellationToken);

        A.CallTo(() => _hub.EnsurePipelineWorkloadRunningAsync(_target)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AFailedWake_IsLoggedAndDoesNotStopThePublish()
    {
        PipelineRegistration? ignored = null;
        A.CallTo(() => _registry.TryGetPipelineRegistration(TenantId, _target, out ignored)).Returns(false);
        A.CallTo(() => _hub.EnsurePipelineWorkloadRunningAsync(_target)).Throws(new InvalidOperationException("hub down"));

        // The queue is durable; the next wake of the target drains it.
        await _waker.EnsureTargetRunningAsync(TenantId, _target, TestContext.Current.CancellationToken);
    }
}
