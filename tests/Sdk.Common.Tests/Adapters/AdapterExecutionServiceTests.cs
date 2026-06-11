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
    public async Task StartAsync_ReconnectFunction_WhenRegisterThrows_WrapsErrorReportingInTryCatch()
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

        // Act: invoke the reconnect function - should NOT throw even though both calls fail
        var exception = await Record.ExceptionAsync(() => capturedReconnectFunc(true));

        // Assert: errors are caught internally
        Assert.Null(exception);
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
