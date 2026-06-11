using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline;

public class EtlContextAccessorTests(DataPipelineFixture fixture) : IClassFixture<DataPipelineFixture>
{
    [NodeName("DummyNode", 1)]
    private record DummyNodeConfiguration : UnitTestNodeConfiguration;

    [NodeConfiguration(typeof(DummyNodeConfiguration))]
    private class DummyNodeWithContext(NodeDelegate next, IUnitTestContext context) : IPipelineNode
    {
        public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
        {
            context.Node = this;
            var c = nodeContext.GetNodeConfiguration<DummyNodeConfiguration>();
            c.DidRun = true;
            await next(dataContext, nodeContext);
        }
    }

    private record UnitTestNodeConfiguration : NodeConfiguration
    {
        public bool DidRun { get; set; }
    }

    [NodeName("DummyNodeWithoutContext", 1)]
    private record DummyNodeWithoutContextConfiguration : UnitTestNodeConfiguration;


    [NodeConfiguration(typeof(DummyNodeWithoutContextConfiguration))]
    // ReSharper disable once ClassNeverInstantiated.Local
    private class DummyNodeWithoutContext(NodeDelegate next) : IPipelineNode
    {
        public Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
        {
            var c = nodeContext.GetNodeConfiguration<DummyNodeWithoutContextConfiguration>();
            c.DidRun = true;
            return next(dataContext, nodeContext);
        }
    }

    private interface IUnitTestContext : IEtlContext
    {
        DummyNodeWithContext? Node { get; set; }
    }

    private class UnitTestContext : IUnitTestContext
    {
        public DummyNodeWithContext? Node { get; set; }
        public string TenantId { get; } = Guid.NewGuid().ToString();
        public Guid PipelineExecutionId { get; } = default;
        public OctoObjectId DataFlowRtId { get; } = default;
        public RtEntityId PipelineRtEntityId { get; } = default;
        public DateTime? ExternalReceivedDateTime { get; } = null;
        public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

        public IGlobalConfiguration GlobalConfiguration { get; } =
            new GlobalConfiguration(new List<ConfigurationDto>());

        public DateTime TransactionStartedDateTime { get; } = DateTime.UtcNow;
    }

    [Fact]
    public async Task RunDataPipeline_NodeWithoutDependencies_Runs()
    {
        fixture.DataPipelineBuilder
            .RegisterNode<DummyNodeWithoutContext>();

        var services = fixture.Services.BuildServiceProvider();
        var c = new DefaultEtlContext("tenantId", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
            new RtEntityId("System.Communication/Adapter", OctoObjectId.GenerateNewId()), DateTime.UtcNow,
            null,
            new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>());

        var orchestrator = services.GetRequiredService<IEtlDataOrchestrator>();

        var nodeConfig = new DummyNodeWithoutContextConfiguration();
        var pipelineConfigurationRoot = new NodeDefinitionRoot { Transformations = [nodeConfig] };


        await orchestrator.ExecutePipelineAsync(pipelineConfigurationRoot, c);


        Assert.True(nodeConfig.DidRun);
    }

    [Fact]
    public async Task RunDataPipeline_WithContext_RunsAndUsesContext()
    {
        fixture.DataPipelineBuilder
            .RegisterEtlContext<IUnitTestContext>()
            .RegisterNode<DummyNodeWithContext>();

        var services = fixture.Services.BuildServiceProvider();
        var c = new UnitTestContext();

        var orchestrator = services.GetRequiredService<IEtlDataOrchestrator>();

        var nodeConfig = new DummyNodeConfiguration();
        var pipelineConfig = new NodeDefinitionRoot { Transformations = [nodeConfig] };


        await orchestrator.ExecutePipelineAsync<IUnitTestContext>(
            pipelineConfig, c);


        Assert.NotNull(c.Node);
        Assert.True(nodeConfig.DidRun);
    }
}