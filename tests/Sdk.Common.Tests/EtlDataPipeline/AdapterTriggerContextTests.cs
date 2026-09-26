using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.EtlDataPipeline;

public class AdapterTriggerContextTests
{
    private const string TenantId = "test-tenant";

    private readonly IPipelineRegistryService _pipelineRegistryService;
    private readonly IEtlDataOrchestrator _etlDataOrchestrator;
    private readonly IContextCreatorService _contextCreatorService;
    private readonly IPipelineExecutionReporter _executionReporter;
    private readonly AdapterTriggerContext _sut;
    private readonly RtEntityId _pipelineRtEntityId;
    private readonly PipelineRegistration _pipelineRegistration;

    public AdapterTriggerContextTests()
    {
        _pipelineRegistryService = A.Fake<IPipelineRegistryService>();
        _etlDataOrchestrator = A.Fake<IEtlDataOrchestrator>();
        _contextCreatorService = A.Fake<IContextCreatorService>();
        _executionReporter = A.Fake<IPipelineExecutionReporter>();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton(_pipelineRegistryService);
        services.AddSingleton(_etlDataOrchestrator);
        services.AddSingleton(_contextCreatorService);
        services.AddSingleton<IPipelineExecutionReporter>(_executionReporter);
        var serviceProvider = services.BuildServiceProvider();

        _pipelineRtEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        var dataFlowRtId = OctoObjectId.GenerateNewId();
        var nodeContext = A.Fake<INodeContext>();
        var globalConfiguration = A.Fake<IGlobalConfiguration>();

        _pipelineRegistration = new PipelineRegistration(
            TenantId,
            dataFlowRtId,
            _pipelineRtEntityId,
            false,
            new NodeDefinitionRoot { Transformations = new List<NodeConfiguration>() },
            globalConfiguration,
            new Dictionary<string, object?>());

        PipelineRegistration? outRegistration = _pipelineRegistration;
        A.CallTo(() => _pipelineRegistryService.TryGetPipelineRegistration(
                TenantId, _pipelineRtEntityId, out outRegistration!))
            .Returns(true);

        _sut = new AdapterTriggerContext(
            serviceProvider, TenantId, dataFlowRtId, _pipelineRtEntityId,
            nodeContext, globalConfiguration);
    }

    [Fact]
    public async Task EndExecutePipelineAsync_ReportsCompletedStatus()
    {
        // Arrange: start a pipeline execution
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.Properties)
            .Returns(new Dictionary<string, object?>());

        A.CallTo(() => _contextCreatorService.CreateEtlContext<IEtlContext>(
                A<PipelineRegistration>._, A<ExecutePipelineOptions>._, A<Guid>._))
            .Returns(Task.FromResult(etlContext));

        A.CallTo(() => _etlDataOrchestrator.ExecutePipelineAsync(
                A<NodeDefinitionRoot>._, A<IEtlContext>._, null, A<object?>._))
            .Returns(Task.FromResult<object?>("result"));

        A.CallTo(() => _executionReporter.ReportExecutionStartAsync(
                A<RtEntityId>._, A<Guid>._, A<PipelineTriggerType>._, A<DateTime>._, A<string?>._))
            .Returns(Task.CompletedTask);

        var executionId = await _sut.StartExecutePipelineAsync(new ExecutePipelineOptions(DateTime.UtcNow));

        // Act
        var result = await _sut.EndExecutePipelineAsync(executionId);

        // Assert
        A.CallTo(() => _executionReporter.ReportExecutionEndAsync(
                executionId,
                PipelineExecutionStatus.Completed,
                A<DateTime>._,
                A<int>._,
                null,
                null))
            .MustHaveHappenedOnceExactly();

        Assert.Equal("result", result);
    }

    [Fact]
    public async Task EndExecutePipelineAsync_WithExecutionResult_ReportsOutputData()
    {
        // Arrange
        var properties = new Dictionary<string, object?>
        {
            [SetPipelineExecutionResultNode.ExecutionResultPropertyKey] = "{\"data\":\"test\"}"
        };
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.Properties).Returns(properties);

        A.CallTo(() => _contextCreatorService.CreateEtlContext<IEtlContext>(
                A<PipelineRegistration>._, A<ExecutePipelineOptions>._, A<Guid>._))
            .Returns(Task.FromResult(etlContext));

        A.CallTo(() => _etlDataOrchestrator.ExecutePipelineAsync(
                A<NodeDefinitionRoot>._, A<IEtlContext>._, null, A<object?>._))
            .Returns(Task.FromResult<object?>(null));

        A.CallTo(() => _executionReporter.ReportExecutionStartAsync(
                A<RtEntityId>._, A<Guid>._, A<PipelineTriggerType>._, A<DateTime>._, A<string?>._))
            .Returns(Task.CompletedTask);

        var executionId = await _sut.StartExecutePipelineAsync(new ExecutePipelineOptions(DateTime.UtcNow));

        // Act
        await _sut.EndExecutePipelineAsync(executionId);

        // Assert: output data from etlContext.Properties is passed to reporter
        A.CallTo(() => _executionReporter.ReportExecutionEndAsync(
                executionId,
                PipelineExecutionStatus.Completed,
                A<DateTime>._,
                A<int>._,
                null,
                "{\"data\":\"test\"}"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReportStatusAsync_ForwardsTheLineToTheReporter()
    {
        // AB#5385: the trigger context is the node's only handle; the line reaches the controller
        // through the same reporter that carries the execution start/end reports.
        await _sut.ReportStatusAsync("2026-09-26T17:40:12Z · seen 12, imported 12",
            cancellationToken: TestContext.Current.CancellationToken);

        A.CallTo(() => _executionReporter.ReportPipelineStatusAsync(
                _pipelineRtEntityId, "2026-09-26T17:40:12Z · seen 12, imported 12", false, A<DateTime>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReportStatusAsync_ErrorFlagIsForwarded()
    {
        await _sut.ReportStatusAsync("ERROR folder not found", isError: true,
            cancellationToken: TestContext.Current.CancellationToken);

        A.CallTo(() => _executionReporter.ReportPipelineStatusAsync(
                _pipelineRtEntityId, "ERROR folder not found", true, A<DateTime>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReportStatusAsync_ReporterThrows_DoesNotPropagate()
    {
        // A status line must never take a poll loop down.
        A.CallTo(() => _executionReporter.ReportPipelineStatusAsync(
                A<RtEntityId>._, A<string>._, A<bool>._, A<DateTime>._))
            .ThrowsAsync(new InvalidOperationException("hub gone"));

        await _sut.ReportStatusAsync("line", cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportStatusAsync_WithoutReporter_IsANoOp()
    {
        // A host that registered no IPipelineExecutionReporter drops the line silently, exactly
        // like it drops the execution reports.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_pipelineRegistryService);
        services.AddSingleton(_etlDataOrchestrator);
        services.AddSingleton(_contextCreatorService);
        var sut = new AdapterTriggerContext(services.BuildServiceProvider(), TenantId,
            OctoObjectId.GenerateNewId(), _pipelineRtEntityId, A.Fake<INodeContext>(), A.Fake<IGlobalConfiguration>());

        await sut.ReportStatusAsync("line", cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EndExecutePipelineAsync_WhenPipelineFails_ReportsFailedStatus()
    {
        // Arrange
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.Properties)
            .Returns(new Dictionary<string, object?>());

        A.CallTo(() => _contextCreatorService.CreateEtlContext<IEtlContext>(
                A<PipelineRegistration>._, A<ExecutePipelineOptions>._, A<Guid>._))
            .Returns(Task.FromResult(etlContext));

        A.CallTo(() => _etlDataOrchestrator.ExecutePipelineAsync(
                A<NodeDefinitionRoot>._, A<IEtlContext>._, null, A<object?>._))
            .ThrowsAsync(new InvalidOperationException("Pipeline failed"));

        A.CallTo(() => _executionReporter.ReportExecutionStartAsync(
                A<RtEntityId>._, A<Guid>._, A<PipelineTriggerType>._, A<DateTime>._, A<string?>._))
            .Returns(Task.CompletedTask);

        var executionId = await _sut.StartExecutePipelineAsync(new ExecutePipelineOptions(DateTime.UtcNow));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EndExecutePipelineAsync(executionId));

        // The reporter should still be called with Failed status
        A.CallTo(() => _executionReporter.ReportExecutionEndAsync(
                executionId,
                PipelineExecutionStatus.Failed,
                A<DateTime>._,
                A<int>._,
                "Pipeline failed",
                null))
            .MustHaveHappenedOnceExactly();
    }
}
