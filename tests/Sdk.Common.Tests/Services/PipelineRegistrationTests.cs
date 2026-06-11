using Bogus;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Sdk.Common.Tests.Services;

public class PipelineRegistrationTests
{
    private readonly Faker _faker = new();

    private PipelineRegistration CreateTestPipelineRegistration(NodeDefinitionRoot? nodeDefinitionRoot = null)
    {
        return new PipelineRegistration(
            TenantId: _faker.Random.Guid().ToString(),
            DataFlowRtId: OctoObjectId.GenerateNewId(),
            PipelineRtEntityId: new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId()),
            IsDebuggingEnabled: _faker.Random.Bool(),
            NodeDefinitionRoot: nodeDefinitionRoot ?? new NodeDefinitionRoot(),
            GlobalConfiguration: A.Fake<IGlobalConfiguration>(),
            Dictionary: new Dictionary<string, object?>()
        );
    }

    [Fact]
    public void Constructor_ValidParameters_SetsProperties()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var dataFlowRtId = OctoObjectId.GenerateNewId();
        var pipelineRtEntityId = new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId());
        var isDebuggingEnabled = true;
        var nodeDefinitionRoot = new NodeDefinitionRoot();
        var globalConfiguration = A.Fake<IGlobalConfiguration>();
        var dictionary = new Dictionary<string, object?>();

        // Act
        var pipelineRegistration = new PipelineRegistration(
            tenantId,
            dataFlowRtId,
            pipelineRtEntityId,
            isDebuggingEnabled,
            nodeDefinitionRoot,
            globalConfiguration,
            dictionary);

        // Assert
        Assert.Equal(tenantId, pipelineRegistration.TenantId);
        Assert.Equal(dataFlowRtId, pipelineRegistration.DataFlowRtId);
        Assert.Equal(pipelineRtEntityId, pipelineRegistration.PipelineRtEntityId);
        Assert.Equal(isDebuggingEnabled, pipelineRegistration.IsDebuggingEnabled);
        Assert.Equal(nodeDefinitionRoot, pipelineRegistration.NodeDefinitionRoot);
        Assert.Equal(globalConfiguration, pipelineRegistration.GlobalConfiguration);
        Assert.Equal(dictionary, pipelineRegistration.Dictionary);
    }

    [Fact]
    public void RegisterExecution_ValidParameters_ReturnsExecution()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var startedDateTime = DateTime.UtcNow;
        var task = Task.FromResult<object?>("result");

        // Act
        var result = pipelineRegistration.RegisterExecution(executionId, startedDateTime, task);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(executionId, result.PipelineExecutionId);
        Assert.Equal(startedDateTime, result.StartedDateTime);
        Assert.Equal(task, result.ExecutePipelineTask);
    }

    [Fact]
    public void RegisterExecution_MoreThan10Executions_RemovesOldest()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executions = new List<(Guid id, DateTime started)>();

        // Register 12 executions
        for (int i = 0; i < 12; i++)
        {
            var id = Guid.NewGuid();
            var started = DateTime.UtcNow.AddMinutes(-i);
            executions.Add((id, started));
            pipelineRegistration.RegisterExecution(id, started, Task.FromResult<object?>(i));
        }

        // Act & Assert - oldest executions should be removed
        var oldestExecution = executions.OrderBy(x => x.started).First();
        var status = pipelineRegistration.GetPipelineExecutionStatus(oldestExecution.id);
        Assert.Equal(PipelineExecutionStatus.Failed, status); // Not found returns Failed
    }

    [Fact]
    public void GetExecutionPropertyValue_ExistingExecution_ReturnsValue()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var execution = pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, Task.FromResult<object?>("result"));
        execution.Properties["testKey"] = "testValue";

        // Act
        var result = pipelineRegistration.GetExecutionPropertyValue<string>(executionId, "testKey");

        // Assert
        Assert.Equal("testValue", result);
    }

    [Fact]
    public void GetExecutionPropertyValue_NonExistingExecution_ReturnsDefault()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();

        // Act
        var result = pipelineRegistration.GetExecutionPropertyValue<string>(Guid.NewGuid(), "testKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetExecutionPropertyValue_WrongType_ReturnsDefault()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var execution = pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, Task.FromResult<object?>("result"));
        execution.Properties["testKey"] = "stringValue";

        // Act
        var result = pipelineRegistration.GetExecutionPropertyValue<int>(executionId, "testKey");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void GetExecutionPropertyValue_NullValue_ReturnsDefault()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var execution = pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, Task.FromResult<object?>("result"));
        execution.Properties["testKey"] = null;

        // Act
        var result = pipelineRegistration.GetExecutionPropertyValue<string>(executionId, "testKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UnregisterExecutionAsync_ExistingExecution_ReturnsResult()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var expectedResult = "testResult";
        pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, Task.FromResult<object?>(expectedResult));

        // Act
        var result = await pipelineRegistration.UnregisterExecutionAsync(executionId);

        // Assert
        Assert.Equal(expectedResult, result);
        // Verify execution was removed
        var status = pipelineRegistration.GetPipelineExecutionStatus(executionId);
        Assert.Equal(PipelineExecutionStatus.Failed, status);
    }

    [Fact]
    public async Task UnregisterExecutionAsync_NonExistingExecution_ThrowsException()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => pipelineRegistration.UnregisterExecutionAsync(executionId));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public void GetPipelineExecutionStatus_CompletedTask_ReturnsCompleted()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, Task.FromResult<object?>("result"));

        // Act
        var status = pipelineRegistration.GetPipelineExecutionStatus(executionId);

        // Assert
        Assert.Equal(PipelineExecutionStatus.Completed, status);
    }

    [Fact]
    public void GetPipelineExecutionStatus_FaultedTask_ReturnsFailed()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var faultedTask = Task.FromException<object?>(new Exception("Test exception"));
        pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, faultedTask);

        // Act
        var status = pipelineRegistration.GetPipelineExecutionStatus(executionId);

        // Assert
        Assert.Equal(PipelineExecutionStatus.Failed, status);
    }

    [Fact]
    public void GetPipelineExecutionStatus_RunningTask_ReturnsRunning()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();
        var executionId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<object?>();
        pipelineRegistration.RegisterExecution(executionId, DateTime.UtcNow, tcs.Task);

        // Act
        var status = pipelineRegistration.GetPipelineExecutionStatus(executionId);

        // Assert
        Assert.Equal(PipelineExecutionStatus.Running, status);
        
        // Cleanup
        tcs.SetResult(null);
    }

    [Fact]
    public void GetPipelineExecutionStatus_NonExistingExecution_ReturnsFailed()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration();

        // Act
        var status = pipelineRegistration.GetPipelineExecutionStatus(Guid.NewGuid());

        // Assert
        Assert.Equal(PipelineExecutionStatus.Failed, status);
    }

    [Fact]
    public async Task StartTriggerPipelineNodesAsync_NoTriggers_ThrowsException()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration(new NodeDefinitionRoot());
        var serviceProvider = A.Fake<IServiceProvider>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => pipelineRegistration.StartTriggerPipelineNodesAsync(serviceProvider));

        Assert.Contains("trigger missing", exception.Message.ToLower());
    }

    [Fact]
    public async Task StartTriggerPipelineNodesAsync_AlreadyRegistered_ThrowsException()
    {
        // Arrange
        var triggerConfig = A.Fake<TriggerNodeConfiguration>();
        var nodeDefinitionRoot = new NodeDefinitionRoot { Triggers = [triggerConfig] };
        var pipelineRegistration = CreateTestPipelineRegistration(nodeDefinitionRoot);
        
        var serviceProvider = A.Fake<IServiceProvider>();
        var contextCreatorService = A.Fake<IContextCreatorService>();
        var nodeLookupService = A.Fake<INodeLookupService>();
        var logger = A.Fake<IPipelineLogger>();
        var triggerNode = A.Fake<ITriggerPipelineNode>();

        A.CallTo(() => serviceProvider.GetService(typeof(IContextCreatorService))).Returns(contextCreatorService);
        A.CallTo(() => serviceProvider.GetService(typeof(INodeLookupService))).Returns(nodeLookupService);
        A.CallTo(() => serviceProvider.GetService(typeof(IPipelineLogger))).Returns(logger);

        string? nodeQualifiedName = "TestNode";
        A.CallTo(() => nodeLookupService.TryGetNodeConfigurationQualifiedName(A<Type>._, out nodeQualifiedName))
            .Returns(true);
        A.CallTo(() => nodeLookupService.TryCreateInstance(serviceProvider, nodeQualifiedName, out triggerNode))
            .Returns(true);

        var triggerContext = A.Fake<ITriggerContext>();
        A.CallTo(() => contextCreatorService.CreateTriggerContext(
                A<string>._, A<OctoObjectId>._, A<RtEntityId>._, A<INodeContext>._, A<IGlobalConfiguration>._))
            .Returns(triggerContext);

        // The First call should succeed
        await pipelineRegistration.StartTriggerPipelineNodesAsync(serviceProvider);

        // Act & Assert - Second call should fail
        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => pipelineRegistration.StartTriggerPipelineNodesAsync(serviceProvider));

        Assert.Contains("already registered", exception.Message.ToLower());
    }

    [Fact]
    public async Task StopTriggerPipelineNodesAsync_NoTriggers_ThrowsException()
    {
        // Arrange
        var pipelineRegistration = CreateTestPipelineRegistration(new NodeDefinitionRoot());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => pipelineRegistration.StopTriggerPipelineNodesAsync());

        Assert.Contains("trigger missing", exception.Message.ToLower());
    }
}
