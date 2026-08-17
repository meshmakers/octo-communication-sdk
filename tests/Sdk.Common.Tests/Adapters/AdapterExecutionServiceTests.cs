using Bogus;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

public class AdapterExecutionServiceTests
{
    private readonly IAdapterHubClient _hubClient;
    private readonly IAdapterService _adapterService;
    private readonly IAdapterHubCallbackService _callbackService;
    private readonly IPipelineRegistryService _pipelineRegistryService;
    private readonly IPipelineExecutionReporter _executionReporter;
    private readonly IOptions<AdapterOptions> _adapterOptions;
    private readonly AdapterExecutionService _service;

    public AdapterExecutionServiceTests()
    {
        _hubClient = A.Fake<IAdapterHubClient>();
        _adapterService = A.Fake<IAdapterService>();
        _callbackService = A.Fake<IAdapterHubCallbackService>();
        _pipelineRegistryService = A.Fake<IPipelineRegistryService>();
        _executionReporter = A.Fake<IPipelineExecutionReporter>();

        var adapterRtId = OctoObjectId.GenerateNewId().ToString();
        _adapterOptions = Options.Create(new AdapterOptions
        {
            AdapterRtId = adapterRtId,
            AdapterCkTypeId = "System.Communication/Adapter",
            TenantId = "testTenant",
            CommunicationControllerServicesUri = "https://localhost:5015"
        });

        var applicationLifetime = A.Fake<IHostApplicationLifetime>();
        var lifetimeManagement = new AdapterLifetimeManagement(applicationLifetime);

        _service = new AdapterExecutionService(
            _hubClient,
            _adapterOptions,
            _adapterService,
            _callbackService,
            lifetimeManagement,
            _pipelineRegistryService,
            _executionReporter);
    }

    private AdapterConfigurationDto CreateTestAdapterConfiguration()
    {
        var rtEntityId = new RtEntityId("System.Communication/Adapter", OctoObjectId.GenerateNewId());
        return new AdapterConfigurationDto(rtEntityId, null, new List<PipelineConfigurationDto>());
    }

    [Fact]
    public async Task StartAsync_ReconnectFunction_WhenSendDeploymentThrowsObjectDisposed_DoesNotThrow()
    {
        // Arrange: capture the reconnect function from StartAsync -> StartCommunicationAsync -> _hubClient.StartAsync
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());

        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .Returns(true);

        // Initial start succeeds
        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Now simulate reconnect where SendDeploymentUpdateResultAsync throws ObjectDisposedException
        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _executionReporter.GetInterruptedExecutionIdsAsync())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Throws(new ObjectDisposedException("HubConnection"));

        // Act: invoke the reconnect function (isReconnect=true) - should NOT throw
        var exception = await Record.ExceptionAsync(() => capturedReconnectFunc(true));

        // Assert: the ObjectDisposedException is caught and does not propagate
        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_ReconnectFunction_WhenRegisterThrows_ReportsBestEffortAndRethrows()
    {
        // Arrange
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .Returns(true);

        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Now on reconnect: RegisterAdapterAsync throws, and error reporting also throws
        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Throws(new InvalidOperationException("Registration failed"));
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Throws(new ObjectDisposedException("HubConnection"));

        // Act & Assert: the failed error report is swallowed (best effort), but the ORIGINAL
        // registration failure must propagate — the SignalR reconnect loop uses it to keep
        // retrying. Swallowing it made the loop treat a failed registration as success and exit,
        // leaving the adapter permanently unregistered (AB#4805).
        var exception = await Record.ExceptionAsync(() => capturedReconnectFunc(true));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Registration failed", exception.Message);
    }

    [Fact]
    public async Task StartAsync_ReconnectFunction_SuccessfulReconnect_SendsDeploymentResult()
    {
        // Arrange
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .Returns(true);

        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Setup for reconnect
        A.CallTo(() => _executionReporter.GetInterruptedExecutionIdsAsync())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Returns(Task.CompletedTask);

        // Act
        await capturedReconnectFunc(true);

        // Assert: deployment result was sent with success
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(
                A<RtEntityId>._,
                A<DeploymentResult>.That.Matches(r => r.IsSuccess)))
            .MustHaveHappenedOnceOrMore();
    }

    [Fact]
    public async Task StartAsync_ReconnectFunction_HandlesInterruptedExecutions()
    {
        // Arrange
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .Returns(true);

        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Setup for reconnect with interrupted executions
        var executionId = Guid.NewGuid();
        A.CallTo(() => _executionReporter.GetInterruptedExecutionIdsAsync())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { executionId.ToString() }));
        A.CallTo(() => _executionReporter.ReportInterruptedExecutionResultAsync(
                A<Guid>._, A<PipelineExecutionStatus>._, A<DateTime>._, A<int>._, A<string?>._))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Returns(Task.CompletedTask);

        // Act
        await capturedReconnectFunc(true);

        // Assert: interrupted execution was reported
        A.CallTo(() => _executionReporter.ReportInterruptedExecutionResultAsync(
                executionId,
                A<PipelineExecutionStatus>._,
                A<DateTime>._,
                A<int>._,
                A<string?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StartAsync_FreshStartup_ResolvesOrphanedExecutions()
    {
        // AB#4280: a fresh adapter process (isReconnect=false) must ask the controller to fail any
        // of its executions that predate the process, since their in-memory tasks were lost on
        // restart. The reconnect path (isReconnect=true) must NOT trigger orphan resolution — there
        // the live local tasks are still owned by this process.
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _executionReporter.FailOrphanedExecutionsAsync(A<DateTime>._))
            .Returns(Task.FromResult(0));
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Returns(Task.CompletedTask);

        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Act: fresh startup (not a reconnect)
        await capturedReconnectFunc(false);

        // Assert: orphan resolution was requested exactly once for the fresh process.
        A.CallTo(() => _executionReporter.FailOrphanedExecutionsAsync(A<DateTime>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StartAsync_Reconnect_DoesNotResolveOrphanedExecutions()
    {
        // The reconnect path keeps its live local tasks; orphan resolution must not run there.
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _executionReporter.GetInterruptedExecutionIdsAsync())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Returns(Task.CompletedTask);

        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Act: reconnect
        await capturedReconnectFunc(true);

        // Assert: no orphan resolution on reconnect
        A.CallTo(() => _executionReporter.FailOrphanedExecutionsAsync(A<DateTime>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task AdapterConfigurationUpdatedAsync_UsesSelectivePipelineUpdate()
    {
        // Arrange
        var deploymentResultSent = new TaskCompletionSource<bool>();

        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Invokes(() => deploymentResultSent.TrySetResult(true))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                A<string>._, A<ICollection<PipelineConfigurationDto>>._, A<List<DeploymentUpdateErrorMessageDto>>._))
            .Returns(true);

        var configuration = CreateTestAdapterConfiguration();

        // Act - runs on background thread
        await _service.AdapterConfigurationUpdatedAsync("testTenant", configuration);

        // Wait for the background task to complete
        var completed = await Task.WhenAny(deploymentResultSent.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Equal(deploymentResultSent.Task, completed);

        // Assert: UpdatePipelinesAsync was called instead of Shutdown+Startup
        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                "testTenant",
                configuration.Pipelines,
                A<List<DeploymentUpdateErrorMessageDto>>._))
            .MustHaveHappenedOnceExactly();

        // Assert: ShutdownAsync and StartupAsync were NOT called
        A.CallTo(() => _adapterService.ShutdownAsync(A<AdapterShutdown>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        // Assert: Deployment result was sent
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(
                A<RtEntityId>._,
                A<DeploymentResult>.That.Matches(r => r.IsSuccess)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AdapterConfigurationUpdatedAsync_NotifiesAdapterServiceBeforePipelineUpdate()
    {
        // Arrange
        var deploymentResultSent = new TaskCompletionSource<bool>();

        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Invokes(() => deploymentResultSent.TrySetResult(true))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _adapterService.ConfigurationUpdatedAsync(
                A<string>._, A<AdapterConfigurationDto>._, A<CancellationToken>._))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                A<string>._, A<ICollection<PipelineConfigurationDto>>._, A<List<DeploymentUpdateErrorMessageDto>>._))
            .Returns(true);

        var configuration = CreateTestAdapterConfiguration();

        // Act - runs on background thread
        await _service.AdapterConfigurationUpdatedAsync("testTenant", configuration);

        var completed = await Task.WhenAny(deploymentResultSent.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Equal(deploymentResultSent.Task, completed);

        // Assert: the adapter service is notified of the adapter-level configuration update BEFORE
        // the selective pipeline update runs, so a (re)registered pipeline sees the new configuration.
        A.CallTo(() => _adapterService.ConfigurationUpdatedAsync(
                "testTenant", configuration, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                    "testTenant", configuration.Pipelines, A<List<DeploymentUpdateErrorMessageDto>>._))
                .MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task AdapterConfigurationUpdatedAsync_LockTimeout_UpdateIsAppliedByInProgressUpdate()
    {
        // AB#4559: an update that runs into the configuration-update lock timeout must not be
        // discarded. It stays queued (last-writer-wins) and is drained by the in-progress update.
        _service.ConfigurationUpdateLockTimeout = TimeSpan.FromMilliseconds(100);

        var firstUpdateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedPipelines = new List<ICollection<PipelineConfigurationDto>>();

        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                A<string>._, A<ICollection<PipelineConfigurationDto>>._, A<List<DeploymentUpdateErrorMessageDto>>._))
            .ReturnsLazily(async (string _, ICollection<PipelineConfigurationDto> pipelines,
                List<DeploymentUpdateErrorMessageDto> _) =>
            {
                lock (appliedPipelines)
                {
                    appliedPipelines.Add(pipelines);
                }

                firstUpdateEntered.TrySetResult();
                await releaseFirstUpdate.Task;
                return true;
            });

        var firstConfiguration = CreateTestAdapterConfiguration();
        var secondConfiguration = CreateTestAdapterConfiguration();

        // Act: first update blocks inside the pipeline update while holding the lock
        await _service.AdapterConfigurationUpdatedAsync("testTenant", firstConfiguration);
        var entered = await Task.WhenAny(firstUpdateEntered.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Equal(firstUpdateEntered.Task, entered);

        // Second update arrives, waits on the lock and runs into the 100ms timeout
        await _service.AdapterConfigurationUpdatedAsync("testTenant", secondConfiguration);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Unblock the first update; it must drain and apply the queued second update
        releaseFirstUpdate.TrySetResult();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (appliedPipelines)
            {
                if (appliedPipelines.Contains(secondConfiguration.Pipelines))
                {
                    break;
                }
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        // Assert: both configurations were applied, the timed-out one was NOT discarded
        lock (appliedPipelines)
        {
            Assert.Equal(2, appliedPipelines.Count);
            Assert.Same(firstConfiguration.Pipelines, appliedPipelines[0]);
            Assert.Same(secondConfiguration.Pipelines, appliedPipelines[1]);
        }

        // And no failure result was sent for the lock timeout
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(
                A<RtEntityId>._,
                A<DeploymentResult>.That.Matches(r => !r.IsSuccess)))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task InitialStartup_HoldsConfigurationLock_ReconcilePushIsAppliedAfterStartup()
    {
        // Regression guard (AB#4806): the register-return configuration (applied via
        // Shutdown+Startup in the reconnect function) and the controller's reconcile push
        // (AdapterConfigurationUpdatedAsync, fired on every registration since AB#4594) used to
        // race — pipelines got registered twice and orphaned bus consumers blocked the exclusive
        // command queues with RESOURCE_LOCKED. The initial startup must hold the configuration
        // update lock; a push arriving meanwhile is parked and applied strictly AFTER startup.
        Func<bool, Task>? capturedReconnectFunc = null;
        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);
        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());

        var startupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._,
                A<CancellationToken>._))
            .ReturnsLazily(async (AdapterStartup _, List<DeploymentUpdateErrorMessageDto> _, CancellationToken _) =>
            {
                startupEntered.TrySetResult();
                await releaseStartup.Task;
                return true;
            });
        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                A<string>._, A<ICollection<PipelineConfigurationDto>>._, A<List<DeploymentUpdateErrorMessageDto>>._))
            .Returns(true);

        await _service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedReconnectFunc);

        // Act: initial registration runs and blocks inside StartupAsync while holding the lock
        var initialStartupTask = capturedReconnectFunc!(false);
        var entered = await Task.WhenAny(startupEntered.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Equal(startupEntered.Task, entered);

        // A reconcile push arrives while startup is still running
        var pushedConfiguration = CreateTestAdapterConfiguration();
        await _service.AdapterConfigurationUpdatedAsync("testTenant", pushedConfiguration);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        // The push must NOT have been applied concurrently with the running startup
        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                A<string>._, A<ICollection<PipelineConfigurationDto>>._, A<List<DeploymentUpdateErrorMessageDto>>._))
            .MustNotHaveHappened();

        // Unblock startup — the parked push must now be applied exactly once
        releaseStartup.TrySetResult();
        await initialStartupTask;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                        "testTenant", pushedConfiguration.Pipelines, A<List<DeploymentUpdateErrorMessageDto>>._))
                    .MustHaveHappenedOnceExactly();
                break;
            }
            catch (FakeItEasy.ExpectationException)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        A.CallTo(() => _pipelineRegistryService.UpdatePipelinesAsync(
                "testTenant", pushedConfiguration.Pipelines, A<List<DeploymentUpdateErrorMessageDto>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task PreUpdateTenantAsync_StopsAndRestartsAdapter()
    {
        // Arrange
        var shutdownCalled = new TaskCompletionSource<bool>();
        Func<bool, Task>? capturedReconnectFunc = null;

        A.CallTo(() => _hubClient.StartAsync(A<Func<bool, Task>>._, A<CancellationToken>._))
            .Invokes((Func<bool, Task> func, CancellationToken _) => capturedReconnectFunc = func)
            .Returns(Task.CompletedTask);

        A.CallTo(() => _hubClient.RegisterAdapterAsync(A<RtEntityId>._))
            .Returns(CreateTestAdapterConfiguration());
        A.CallTo(() => _adapterService.StartupAsync(A<AdapterStartup>._, A<List<DeploymentUpdateErrorMessageDto>>._, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _hubClient.SendDeploymentUpdateResultAsync(A<RtEntityId>._, A<DeploymentResult>._))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _executionReporter.GetInterruptedExecutionIdsAsync())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        A.CallTo(() => _adapterService.ShutdownAsync(A<AdapterShutdown>._, A<CancellationToken>._))
            .Invokes(() => shutdownCalled.TrySetResult(true))
            .Returns(Task.CompletedTask);

        // Initial start
        await _service.StartAsync(CancellationToken.None);

        // Act - PreUpdateTenantAsync runs on a background thread
        await _service.PreUpdateTenantAsync("testTenant");

        // Wait for the background task to call ShutdownAsync
        var completed = await Task.WhenAny(shutdownCalled.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
        Assert.Equal(shutdownCalled.Task, completed);

        // Assert: ShutdownAsync was called (from StopAsync)
        A.CallTo(() => _adapterService.ShutdownAsync(
                A<AdapterShutdown>.That.Matches(s => s.TenantId == "testTenant"),
                A<CancellationToken>._))
            .MustHaveHappened();
    }
}
