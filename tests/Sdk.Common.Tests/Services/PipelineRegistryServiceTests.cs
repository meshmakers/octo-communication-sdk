using Bogus;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.Services;

public class PipelineRegistryServiceTests
{
    private readonly Faker _faker = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly IPipelineConfigurationSerializer _configurationSerializer;
    private readonly PipelineRegistryService _service;
    private readonly ILoggerFactory _loggerFactory;

    public PipelineRegistryServiceTests(ITestOutputHelper testOutputHelper)
    {
        _serviceProvider = A.Fake<IServiceProvider>();
        _configurationSerializer = A.Fake<IPipelineConfigurationSerializer>();
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddXUnit(testOutputHelper); // Redirect logs to xUnit test output
        });

        _service = new PipelineRegistryService(_loggerFactory.CreateLogger<PipelineRegistryService>(), _serviceProvider,
            _configurationSerializer);

        // Setup basic service provider mocks
        var contextCreatorService = A.Fake<IContextCreatorService>();
        var nodeLookupService = A.Fake<INodeLookupService>();
        var logger = A.Fake<IPipelineLogger>();

        A.CallTo(() => _serviceProvider.GetService(typeof(IContextCreatorService))).Returns(contextCreatorService);
        A.CallTo(() => _serviceProvider.GetService(typeof(INodeLookupService))).Returns(nodeLookupService);
        A.CallTo(() => _serviceProvider.GetService(typeof(IPipelineLogger))).Returns(logger);
    }

    private PipelineConfigurationDto CreateTestPipelineConfiguration(string nodeConfiguration = "test-configuration")
    {
        return new PipelineConfigurationDto(OctoObjectId.GenerateNewId(),
            new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId()),
            _faker.Random.Bool(), nodeConfiguration,
            new List<ConfigurationDto>());
    }

    private NodeDefinitionRoot CreateTestNodeDefinitionRoot(bool includeTriggers = true)
    {
        var triggerConfig = includeTriggers ? new[] { A.Fake<TriggerNodeConfiguration>() } : null;
        return new NodeDefinitionRoot { Triggers = triggerConfig };
    }

    [Fact]
    public async Task RegisterPipelineAsync_ValidConfiguration_Success()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineConfig = CreateTestPipelineConfiguration();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(pipelineConfig.NodeConfiguration))
            .Returns(nodeDefinitionRoot);

        // Setup mocks for trigger node creation
        var triggerNode = A.Fake<ITriggerPipelineNode>();
        var nodeLookupService = A.Fake<INodeLookupService>();
        string? nodeQualifiedName = "TestTriggerNode";

        A.CallTo(() => _serviceProvider.GetService(typeof(INodeLookupService))).Returns(nodeLookupService);
        A.CallTo(() => nodeLookupService.TryGetNodeConfigurationQualifiedName(A<Type>._, out nodeQualifiedName))
            .Returns(true);
        A.CallTo(() => nodeLookupService.TryCreateInstance(_serviceProvider, nodeQualifiedName, out triggerNode))
            .Returns(true);

        var contextCreatorService = A.Fake<IContextCreatorService>();
        var triggerContext = A.Fake<ITriggerContext>();
        A.CallTo(() => _serviceProvider.GetService(typeof(IContextCreatorService))).Returns(contextCreatorService);
        A.CallTo(() => contextCreatorService.CreateTriggerContext(
                A<string>._, A<OctoObjectId>._, A<RtEntityId>._, A<INodeContext>._, A<IGlobalConfiguration>._))
            .Returns(triggerContext);

        // Act
        await _service.RegisterPipelineAsync(tenantId, pipelineConfig);

        // Assert
        Assert.True(_service.IsRegistered(tenantId, pipelineConfig.PipelineRtEntityId));
        Assert.True(_service.TryGetPipelineRegistration(tenantId, pipelineConfig.PipelineRtEntityId,
            out var registration));
        Assert.NotNull(registration);
        Assert.Equal(tenantId, registration.TenantId);
        Assert.Equal(pipelineConfig.DataFlowRtId, registration.DataFlowRtId);
        Assert.Equal(pipelineConfig.PipelineRtEntityId, registration.PipelineRtEntityId);
    }

    [Fact]
    public async Task RegisterPipelineAsync_NoTriggers_ThrowsException()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineConfig = CreateTestPipelineConfiguration();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot(includeTriggers: false);

        A.CallTo(() => _configurationSerializer.DeserializeAsync(pipelineConfig.NodeConfiguration))
            .Returns(nodeDefinitionRoot);

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<PipelineExecutionException>(() =>
                _service.RegisterPipelineAsync(tenantId, pipelineConfig));

        Assert.Contains("trigger missing", exception.Message.ToLower());
    }

    [Fact]
    public async Task RegisterPipelinesAsync_AllValid_ReturnsTrue()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineConfigs = new[]
        {
            CreateTestPipelineConfiguration(),
            CreateTestPipelineConfiguration()
        };
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(nodeDefinitionRoot);

        // Setup mocks for trigger nodes
        SetupTriggerNodeMocks();

        // Act
        var result = await _service.RegisterPipelinesAsync(tenantId, pipelineConfigs, errorMessages);

        // Assert
        Assert.True(result);
        Assert.Empty(errorMessages);
        Assert.True(_service.IsRegistered(tenantId, pipelineConfigs[0].PipelineRtEntityId));
        Assert.True(_service.IsRegistered(tenantId, pipelineConfigs[1].PipelineRtEntityId));
    }

    [Fact]
    public async Task RegisterPipelinesAsync_OneInvalid_ReturnsFalseAndAddsError()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var validConfig = CreateTestPipelineConfiguration("{\"trigger\": []}");
        var invalidConfig = CreateTestPipelineConfiguration();
        var pipelineConfigs = new[] { validConfig, invalidConfig };
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();

        var validNodeDefinitionRoot = CreateTestNodeDefinitionRoot();
        A.CallTo(() => _configurationSerializer.DeserializeAsync(validConfig.NodeConfiguration))
            .Returns(validNodeDefinitionRoot);
        A.CallTo(() => _configurationSerializer.DeserializeAsync(invalidConfig.NodeConfiguration))
            .Throws<PipelineSerializationException>();

        SetupTriggerNodeMocks();

        // Act
        var result = await _service.RegisterPipelinesAsync(tenantId, pipelineConfigs, errorMessages);

        // Assert
        Assert.False(result);
        Assert.Single(errorMessages);
        Assert.Equal(invalidConfig.PipelineRtEntityId, errorMessages[0].PipelineRtEntityId);
        Assert.Equal(invalidConfig.DataFlowRtId, errorMessages[0].DataFlowRtId);
        Assert.True(_service.IsRegistered(tenantId, validConfig.PipelineRtEntityId));
        Assert.False(_service.IsRegistered(tenantId, invalidConfig.PipelineRtEntityId));
    }

    [Fact]
    public async Task UnregisterPipelineAsync_ExistingPipeline_RemovesPipeline()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineConfig = CreateTestPipelineConfiguration();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(pipelineConfig.NodeConfiguration))
            .Returns(nodeDefinitionRoot);

        SetupTriggerNodeMocks();

        // Register pipeline first
        await _service.RegisterPipelineAsync(tenantId, pipelineConfig);
        Assert.True(_service.IsRegistered(tenantId, pipelineConfig.PipelineRtEntityId));

        // Act
        await _service.UnregisterPipelineAsync(tenantId, pipelineConfig.PipelineRtEntityId);

        // Assert
        Assert.False(_service.IsRegistered(tenantId, pipelineConfig.PipelineRtEntityId));
        Assert.False(_service.TryGetPipelineRegistration(tenantId, pipelineConfig.PipelineRtEntityId, out _));
    }

    [Fact]
    public async Task UnregisterPipelineAsync_NonExistingPipeline_DoesNotThrow()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineRtEntityId = new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId());

        // Act & Assert - Should not throw
        await _service.UnregisterPipelineAsync(tenantId, pipelineRtEntityId);
    }

    [Fact]
    public async Task UnregisterAllPipelinesAsync_MultiplePipelines_RemovesAll()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineConfigs = new[]
        {
            CreateTestPipelineConfiguration(),
            CreateTestPipelineConfiguration()
        };
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(nodeDefinitionRoot);

        SetupTriggerNodeMocks();

        // Register pipelines
        foreach (var config in pipelineConfigs)
        {
            await _service.RegisterPipelineAsync(tenantId, config);
        }

        Assert.True(_service.IsRegistered(tenantId, pipelineConfigs[0].PipelineRtEntityId));
        Assert.True(_service.IsRegistered(tenantId, pipelineConfigs[1].PipelineRtEntityId));

        // Act
        await _service.UnregisterAllPipelinesAsync(tenantId);

        // Assert
        Assert.False(_service.IsRegistered(tenantId, pipelineConfigs[0].PipelineRtEntityId));
        Assert.False(_service.IsRegistered(tenantId, pipelineConfigs[1].PipelineRtEntityId));
    }

    [Fact]
    public async Task UnregisterAllPipelinesAsync_DifferentTenants_OnlyRemovesSpecificTenant()
    {
        // Arrange
        var tenantId1 = _faker.Random.Guid().ToString();
        var tenantId2 = _faker.Random.Guid().ToString();
        var pipelineConfig1 = CreateTestPipelineConfiguration();
        var pipelineConfig2 = CreateTestPipelineConfiguration();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(nodeDefinitionRoot);

        SetupTriggerNodeMocks();

        // Register pipelines for different tenants
        await _service.RegisterPipelineAsync(tenantId1, pipelineConfig1);
        await _service.RegisterPipelineAsync(tenantId2, pipelineConfig2);

        // Act
        await _service.UnregisterAllPipelinesAsync(tenantId1);

        // Assert
        Assert.False(_service.IsRegistered(tenantId1, pipelineConfig1.PipelineRtEntityId));
        Assert.True(_service.IsRegistered(tenantId2, pipelineConfig2.PipelineRtEntityId));
    }

    [Fact]
    public void IsRegistered_NonExistingPipeline_ReturnsFalse()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineRtEntityId = new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId());

        // Act
        var result = _service.IsRegistered(tenantId, pipelineRtEntityId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryGetPipelineRegistration_ExistingPipeline_ReturnsTrue()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineConfig = CreateTestPipelineConfiguration();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(pipelineConfig.NodeConfiguration))
            .Returns(nodeDefinitionRoot);

        SetupTriggerNodeMocks();

        await _service.RegisterPipelineAsync(tenantId, pipelineConfig);

        // Act
        var result =
            _service.TryGetPipelineRegistration(tenantId, pipelineConfig.PipelineRtEntityId, out var registration);

        // Assert
        Assert.True(result);
        Assert.NotNull(registration);
        Assert.Equal(tenantId, registration.TenantId);
        Assert.Equal(pipelineConfig.PipelineRtEntityId, registration.PipelineRtEntityId);
    }

    [Fact]
    public void TryGetPipelineRegistration_NonExistingPipeline_ReturnsFalse()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineRtEntityId = new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId());

        // Act
        var result = _service.TryGetPipelineRegistration(tenantId, pipelineRtEntityId, out var registration);

        // Assert
        Assert.False(result);
        Assert.Null(registration);
    }

    [Fact]
    public async Task RegisterPipelinesAsync_ClearsExistingRegistrations()
    {
        // Arrange
        var tenantId = _faker.Random.Guid().ToString();
        var firstConfig = CreateTestPipelineConfiguration();
        var secondConfig = CreateTestPipelineConfiguration();
        var nodeDefinitionRoot = CreateTestNodeDefinitionRoot();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(nodeDefinitionRoot);

        SetupTriggerNodeMocks();

        // Register first pipeline
        await _service.RegisterPipelineAsync(tenantId, firstConfig);
        Assert.True(_service.IsRegistered(tenantId, firstConfig.PipelineRtEntityId));

        // Act - Register a new set of pipelines
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();
        await _service.RegisterPipelinesAsync(tenantId, [secondConfig], errorMessages);

        // Assert
        Assert.False(_service.IsRegistered(tenantId, firstConfig.PipelineRtEntityId));
        Assert.True(_service.IsRegistered(tenantId, secondConfig.PipelineRtEntityId));
    }

    [Fact]
    public async Task RegisterPipelineAsync_ExistingRegistration_ReplacesStaleRegistrationAndStopsOldTriggers()
    {
        // AB#4559: a re-registration without prior unregister must replace the surviving
        // registration (old GlobalConfiguration) instead of silently keeping it while the DTO map
        // already holds the new configuration.
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineRtEntityId = new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId());
        var dataFlowRtId = OctoObjectId.GenerateNewId();
        var configurationRtId = OctoObjectId.GenerateNewId();
        var oldConfig = new PipelineConfigurationDto(dataFlowRtId, pipelineRtEntityId, false, "test-configuration",
            [new ConfigurationDto(configurationRtId, new RtCkId<CkTypeId>("TestModel/TestConfig"), "ApiConfig", "\"old\"")]);
        var newConfig = new PipelineConfigurationDto(dataFlowRtId, pipelineRtEntityId, false, "test-configuration",
            [new ConfigurationDto(configurationRtId, new RtCkId<CkTypeId>("TestModel/TestConfig"), "ApiConfig", "\"new\"")]);

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(CreateTestNodeDefinitionRoot());
        var triggerNode = SetupTriggerNodeMocks();

        await _service.RegisterPipelineAsync(tenantId, oldConfig);

        // Act: register again without unregistering first (the stale-registration path)
        await _service.RegisterPipelineAsync(tenantId, newConfig);

        // Assert: the active registration serves the NEW configuration, the old triggers are stopped
        Assert.True(_service.TryGetPipelineRegistration(tenantId, pipelineRtEntityId, out var registration));
        Assert.Equal("\"new\"", registration!.GlobalConfiguration.GetRawJson("ApiConfig"));
        A.CallTo(() => triggerNode.StopAsync(A<ITriggerContext>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => triggerNode.StartAsync(A<ITriggerContext>._)).MustHaveHappenedTwiceExactly();

        // A further selective update with the identical configuration is correctly detected as
        // unchanged — no additional trigger start.
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();
        var success = await _service.UpdatePipelinesAsync(tenantId, [newConfig], errorMessages);
        Assert.True(success);
        Assert.Empty(errorMessages);
        A.CallTo(() => triggerNode.StartAsync(A<ITriggerContext>._)).MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task UpdatePipelinesAsync_ChangedConfiguration_MaterializesNewGlobalConfiguration()
    {
        // AB#4559 signature: after a config entity change, a redeploy must materialize the new
        // GlobalConfiguration for executions.
        var tenantId = _faker.Random.Guid().ToString();
        var pipelineRtEntityId = new RtEntityId("TestModel/TestType", OctoObjectId.GenerateNewId());
        var dataFlowRtId = OctoObjectId.GenerateNewId();
        var configurationRtId = OctoObjectId.GenerateNewId();
        var oldConfig = new PipelineConfigurationDto(dataFlowRtId, pipelineRtEntityId, false, "test-configuration",
            [new ConfigurationDto(configurationRtId, new RtCkId<CkTypeId>("TestModel/TestConfig"), "ApiConfig", "\"old\"")]);
        var newConfig = new PipelineConfigurationDto(dataFlowRtId, pipelineRtEntityId, false, "test-configuration",
            [new ConfigurationDto(configurationRtId, new RtCkId<CkTypeId>("TestModel/TestConfig"), "ApiConfig", "\"new\"")]);

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(CreateTestNodeDefinitionRoot());
        var triggerNode = SetupTriggerNodeMocks();

        await _service.RegisterPipelineAsync(tenantId, oldConfig);

        // Act
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();
        var success = await _service.UpdatePipelinesAsync(tenantId, [newConfig], errorMessages);

        // Assert
        Assert.True(success);
        Assert.True(_service.TryGetPipelineRegistration(tenantId, pipelineRtEntityId, out var registration));
        Assert.Equal("\"new\"", registration!.GlobalConfiguration.GetRawJson("ApiConfig"));
        A.CallTo(() => triggerNode.StopAsync(A<ITriggerContext>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RegisterPipelinesAsync_SurvivingRegistrations_StopsTheirTriggerNodes()
    {
        // AB#4559: the full re-registration must stop trigger nodes of surviving registrations
        // instead of clearing the dictionaries and leaking the running nodes.
        var tenantId = _faker.Random.Guid().ToString();
        var firstConfig = CreateTestPipelineConfiguration();
        var secondConfig = CreateTestPipelineConfiguration();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(CreateTestNodeDefinitionRoot());
        var triggerNode = SetupTriggerNodeMocks();

        await _service.RegisterPipelineAsync(tenantId, firstConfig);

        // Act
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();
        await _service.RegisterPipelinesAsync(tenantId, [secondConfig], errorMessages);

        // Assert: the first pipeline's trigger nodes were stopped, not leaked
        A.CallTo(() => triggerNode.StopAsync(A<ITriggerContext>._)).MustHaveHappenedOnceExactly();
        Assert.False(_service.IsRegistered(tenantId, firstConfig.PipelineRtEntityId));
        Assert.True(_service.IsRegistered(tenantId, secondConfig.PipelineRtEntityId));
    }

    [Fact]
    public async Task UpdatePipelinesAsync_RemovedPipelineTriggerStopThrows_DoesNotAbortBatch()
    {
        // AB#4761: a removed pipeline whose trigger StopAsync throws (e.g. a FromMicrosoftGraphEmail
        // node whose polling task faulted) must NOT abort the whole selective update. Before the
        // fix the throw propagated out of UpdatePipelinesAsync and flipped the adapter to
        // ConfigurationState=Error; now the update logs and continues, still registering the other
        // pipelines, and never throws.
        var tenantId = _faker.Random.Guid().ToString();
        var toRemoveConfig = CreateTestPipelineConfiguration();
        var newConfig = CreateTestPipelineConfiguration();

        A.CallTo(() => _configurationSerializer.DeserializeAsync(A<string>._))
            .Returns(CreateTestNodeDefinitionRoot());
        var triggerNode = SetupTriggerNodeMocks();
        A.CallTo(() => triggerNode.StopAsync(A<ITriggerContext>._))
            .Throws(new InvalidOperationException("trigger stop boom"));

        // Register the pipeline that the update will drop (its unregister will throw).
        await _service.RegisterPipelineAsync(tenantId, toRemoveConfig);

        // Act: selective update that removes toRemoveConfig and adds newConfig.
        var errorMessages = new List<DeploymentUpdateErrorMessageDto>();
        var exception = await Record.ExceptionAsync(() =>
            _service.UpdatePipelinesAsync(tenantId, [newConfig], errorMessages));

        // Assert: the failing unregister did not abort the batch — no throw, the new pipeline is
        // registered, and the failing StopAsync was actually attempted.
        Assert.Null(exception);
        Assert.True(_service.IsRegistered(tenantId, newConfig.PipelineRtEntityId));
        A.CallTo(() => triggerNode.StopAsync(A<ITriggerContext>._)).MustHaveHappened();
    }

    private ITriggerPipelineNode SetupTriggerNodeMocks()
    {
        var triggerNode = A.Fake<ITriggerPipelineNode>();
        var nodeLookupService = A.Fake<INodeLookupService>();
        string? nodeQualifiedName = "TestTriggerNode";

        A.CallTo(() => _serviceProvider.GetService(typeof(INodeLookupService))).Returns(nodeLookupService);
        A.CallTo(() => nodeLookupService.TryGetNodeConfigurationQualifiedName(A<Type>._, out nodeQualifiedName))
            .Returns(true);
        A.CallTo(() => nodeLookupService.TryCreateInstance(_serviceProvider, nodeQualifiedName, out triggerNode))
            .Returns(true);

        var contextCreatorService = A.Fake<IContextCreatorService>();
        var triggerContext = A.Fake<ITriggerContext>();
        A.CallTo(() => _serviceProvider.GetService(typeof(IContextCreatorService))).Returns(contextCreatorService);
        A.CallTo(() => contextCreatorService.CreateTriggerContext(
                A<string>._, A<OctoObjectId>._, A<RtEntityId>._, A<INodeContext>._, A<IGlobalConfiguration>._))
            .Returns(triggerContext);

        return triggerNode;
    }
}