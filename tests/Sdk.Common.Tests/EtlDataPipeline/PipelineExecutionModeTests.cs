using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// Covers the M4-B.2 SDK contract: <see cref="IPipelineExecutionMode"/> threading and
/// the new <c>RecordDryRunIntent</c> path from <see cref="INodeContext"/> through
/// <see cref="DefaultPipelineDebugger"/> into <see cref="DebugPointDto"/>.
/// Load-node retrofits live in octo-mesh-adapter (Commit 2); these tests pin the
/// SDK-side wiring so the consumer can rely on it.
/// </summary>
public class PipelineExecutionModeTests(DataPipelineFixture fixture, ITestOutputHelper testOutputHelper)
    : IClassFixture<DataPipelineFixture>
{
    [Fact]
    public void PipelineExecutionMode_NullByDefault_OnRootNodeContext()
    {
        var sp = fixture.Services.BuildServiceProvider();
        var logger = sp.GetRequiredService<IPipelineLogger>();
        using var dataContext = new DataContextImpl();

        var root = NodeContext.CreateRootNodeContext(sp, logger, dataContext);

        Assert.Null(root.PipelineExecutionMode);
    }

    [Fact]
    public void PipelineExecutionMode_PropagatesToChildNodeContexts()
    {
        var sp = fixture.Services.BuildServiceProvider();
        var logger = sp.GetRequiredService<IPipelineLogger>();
        using var dataContext = new DataContextImpl();
        var mode = new DefaultPipelineExecutionMode { IsDryRun = true };

        var root = NodeContext.CreateRootNodeContext(sp, logger, dataContext, pipelineExecutionMode: mode);
        var child = root.RegisterChildNode("ChildNode@1",
            new TestOutputNodeConfiguration { TargetPath = "$.x", TargetValue = 1 },
            dataContext);

        Assert.Same(mode, root.PipelineExecutionMode);
        Assert.Same(mode, child.PipelineExecutionMode);
        Assert.True(child.PipelineExecutionMode!.IsDryRun);
    }

    [Fact]
    public void RecordDryRunIntent_NoOp_WhenNoDebuggerAttached()
    {
        var sp = fixture.Services.BuildServiceProvider();
        var logger = sp.GetRequiredService<IPipelineLogger>();
        using var dataContext = new DataContextImpl();

        var root = NodeContext.CreateRootNodeContext(sp, logger, dataContext);

        // Should not throw; the call is observability, not correctness.
        root.RecordDryRunIntent("ApplyChanges@1", new { Path = "$.x", Count = 42 });
    }

    [Fact]
    public void RecordDryRunIntent_WritesPayloadIntoDebugPoint_WhenDebuggerAttached()
    {
        var sp = fixture.Services.BuildServiceProvider();
        var logger = sp.GetRequiredService<IPipelineLogger>();
        using var dataContext = new DataContextImpl();
        var debugger = new DefaultPipelineDebugger(sp.GetRequiredService<ILoggerFactory>());
        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);
        var mode = new DefaultPipelineExecutionMode { IsDryRun = true };

        var root = NodeContext.CreateRootNodeContext(sp, logger, dataContext, debugger, mode);
        root.RecordDryRunIntent("ApplyChanges@1", new { Path = "$.changes", Count = 3 });

        var info = debugger.GetDebugInformation();
        var dp = info.DebugPoints.FirstOrDefault(d => d.NodePath == "PipelineExecution");
        Assert.NotNull(dp);
        Assert.Equal("ApplyChanges@1", dp.DryRunNodeTypeName);
        Assert.NotNull(dp.DryRunIntent);
        var roundTrip = JsonNode.Parse(dp.DryRunIntent!);
        Assert.NotNull(roundTrip);
        Assert.Equal("$.changes", roundTrip!["Path"]!.GetValue<string>());
        Assert.Equal(3, roundTrip["Count"]!.GetValue<int>());
    }

    [Fact]
    public void RecordDryRunIntent_DoesNotThrow_OnNonSerialisablePayload()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var sp = fixture.Services.BuildServiceProvider();
        var logger = sp.GetRequiredService<IPipelineLogger>();
        using var dataContext = new DataContextImpl();
        var debugger = new DefaultPipelineDebugger(sp.GetRequiredService<ILoggerFactory>());
        debugger.RegisterPipelineRtEntityId(
            new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId()), Guid.NewGuid());

        var root = NodeContext.CreateRootNodeContext(sp, logger, dataContext, debugger);

        // CancellationTokenSource exposes circular and non-serialisable members under STJ.
        // The recorder MUST swallow the serialisation error and continue — Load nodes
        // call this from their hot path; a throw would crash the pipeline.
        using var cts = new CancellationTokenSource();
        root.RecordDryRunIntent("Hostile@1", cts);

        // Debug point may still be created (with null intent) or absent — both are acceptable.
        // What matters is that no exception escaped.
        var info = debugger.GetDebugInformation();
        Assert.NotNull(info);
    }

    [Fact]
    public async Task ExecutePipelineAsync_ThreadsExecutionModeIntoNodeContext()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());
        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);
        var observedMode = new ModeCapture();

        var pipeline = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new ModeCaptureNodeConfiguration { Capture = observedMode }
            }
        };

        var ctx = new DefaultEtlContext("dry-run-mode-test", OctoObjectId.GenerateNewId(), pipelineExecutionId,
            pipelineEntityId, DateTime.UtcNow, null,
            new GlobalConfiguration(new List<ConfigurationDto>()),
            new Dictionary<string, object?>());

        await orchestrator.ExecutePipelineAsync(pipeline, ctx, debugger,
            executionMode: new DefaultPipelineExecutionMode { IsDryRun = true });

        Assert.NotNull(observedMode.Observed);
        Assert.True(observedMode.Observed!.IsDryRun);
    }

    [Fact]
    public async Task ExecutePipelineAsync_NullExecutionMode_LeavesNodeContextPipelineExecutionModeNull()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var observedMode = new ModeCapture();

        var pipeline = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new ModeCaptureNodeConfiguration { Capture = observedMode }
            }
        };

        var ctx = new DefaultEtlContext("default-mode-test", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
            new RtEntityId("System.Communication/Adapter", OctoObjectId.GenerateNewId()), DateTime.UtcNow, null,
            new GlobalConfiguration(new List<ConfigurationDto>()),
            new Dictionary<string, object?>());

        await orchestrator.ExecutePipelineAsync(pipeline, ctx);

        Assert.True(observedMode.NodeRan);
        Assert.Null(observedMode.Observed);
    }
}

internal class ModeCapture
{
    public IPipelineExecutionMode? Observed { get; set; }
    public bool NodeRan { get; set; }
}

[NodeName("ModeCapture", 1)]
internal record ModeCaptureNodeConfiguration : NodeConfiguration
{
    public ModeCapture Capture { get; set; } = new();
}

[NodeConfiguration(typeof(ModeCaptureNodeConfiguration))]
internal class ModeCaptureNode(NodeDelegate next) : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var cfg = nodeContext.GetNodeConfiguration<ModeCaptureNodeConfiguration>();
        cfg.Capture.NodeRan = true;
        cfg.Capture.Observed = nodeContext.PipelineExecutionMode;
        await next(dataContext, nodeContext);
    }
}
