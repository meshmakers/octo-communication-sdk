using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// The orchestrator names the caller of the runtime-entity reads its nodes make (AB#9824): one
/// <see cref="RuntimeReadExecution" /> per execution, a node scope with the node's type and path around
/// every node (also child nodes of control nodes), restored when the node returns. The engine reads
/// <see cref="RuntimeReadScope.Current" /> to report an unbounded read once per execution and node.
/// </summary>
public class RuntimeReadScopeTests(DataPipelineFixture fixture, ITestOutputHelper testOutputHelper)
    : IClassFixture<DataPipelineFixture>
{
    [Fact]
    public async Task ExecutePipelineAsync_NodeSeesExecutionAndOwnNode()
    {
        var capture = new ReadScopeCapture();
        var tenantId = "read-scope-tenant";
        var dataFlowId = OctoObjectId.GenerateNewId();
        var executionId = Guid.NewGuid();
        var pipelineId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());

        await ExecuteAsync(new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration> { new ReadScopeCaptureNodeConfiguration { Capture = capture } }
        }, new DefaultEtlContext(tenantId, dataFlowId, executionId, pipelineId, DateTime.UtcNow, null,
            new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>()));

        var seen = Assert.Single(capture.Before);
        Assert.NotNull(seen.Execution);
        Assert.Equal(tenantId, seen.Execution!.TenantId);
        Assert.Equal(pipelineId.RtId.ToString(), seen.Execution.PipelineId);
        Assert.Equal(dataFlowId.ToString(), seen.Execution.DataFlowId);
        Assert.Equal(executionId.ToString(), seen.Execution.ExecutionId);
        Assert.Equal("ReadScopeCapture@1", seen.NodeType);
        Assert.EndsWith("ReadScopeCapture@1", seen.NodePath);
        Assert.False(seen.Suppressed);

        // Nothing leaks out of the execution.
        Assert.Null(RuntimeReadScope.Current);
    }

    [Fact]
    public async Task ExecutePipelineAsync_TwoNodes_EachSeesItsOwnPath_AndTheFirstGetsItsScopeBackAfterNext()
    {
        var first = new ReadScopeCapture();
        var second = new ReadScopeCapture();

        await ExecuteAsync(new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new ReadScopeCaptureNodeConfiguration { Capture = first },
                new ReadScopeCaptureNodeConfiguration { Capture = second }
            }
        }, NewContext());

        var firstBefore = Assert.Single(first.Before);
        var firstAfter = Assert.Single(first.After);
        var secondBefore = Assert.Single(second.Before);

        // Same type in one sequence: the path reads the same, the key (with the sequence numbers) tells them apart,
        // so the engine reports both nodes.
        Assert.Equal(firstBefore.NodePath, secondBefore.NodePath);
        Assert.NotEqual(firstBefore.NodeKey, secondBefore.NodeKey);
        Assert.Same(firstBefore.Execution, secondBefore.Execution);
        Assert.True(firstBefore.Execution!.TryMarkReported(firstBefore.NodeKey));
        Assert.True(firstBefore.Execution.TryMarkReported(secondBefore.NodeKey));

        // The downstream node's scope ended when it returned: reads the first node makes after next()
        // are its own again.
        Assert.Equal(firstBefore.NodeKey, firstAfter.NodeKey);
        Assert.Equal(firstBefore.NodeType, firstAfter.NodeType);
    }

    [Fact]
    public async Task ExecutePipelineAsync_ChildNodeOfForEach_SharesExecutionAndPathAcrossIterations()
    {
        var capture = new ReadScopeCapture();

        await ExecuteAsync(new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new ForEachNodeConfiguration
                {
                    Path = "$.items",
                    IterationPath = "$.items",
                    TargetPath = "$.result",
                    MaxDegreeOfParallelism = 2,
                    Transformations = new List<NodeConfiguration>
                    {
                        new ReadScopeCaptureNodeConfiguration { Capture = capture }
                    }
                }
            }
        }, NewContext(), new JsonObject { ["items"] = new JsonArray(1, 2, 3) });

        Assert.Equal(3, capture.Before.Count);
        var executions = capture.Before.Select(c => c.Execution).Distinct().ToList();
        var execution = Assert.Single(executions);
        Assert.NotNull(execution);

        // Every iteration runs the same node: one path and one key without the iteration segment, so the engine
        // reports it once per execution.
        var path = Assert.Single(capture.Before.Select(c => c.NodePath).Distinct());
        Assert.Equal("PipelineExecution/ForEach@1/ReadScopeCapture@1", path);
        var key = Assert.Single(capture.Before.Select(c => c.NodeKey).Distinct());
        Assert.DoesNotContain("[", key);
        Assert.Equal(1, capture.Before.Count(c => execution!.TryMarkReported(c.NodeKey)));
    }

    [Theory]
    [InlineData("PipelineExecution/ForEach@1/[12]/Node@1", "PipelineExecution/ForEach@1/Node@1")]
    [InlineData("0:PipelineExecution/1:ForEach@1/[3]/0:Node@1", "0:PipelineExecution/1:ForEach@1/0:Node@1")]
    [InlineData("PipelineExecution/For@1/[0]/If@1/[1]/Node@1", "PipelineExecution/For@1/If@1/Node@1")]
    [InlineData("PipelineExecution/Node@1", "PipelineExecution/Node@1")]
    [InlineData("PipelineExecution/[x]/Node@1", "PipelineExecution/[x]/Node@1")]
    [InlineData("PipelineExecution/[]/Node@1", "PipelineExecution/[]/Node@1")]
    public void WithoutIterationSegments_RemovesOnlyNumericIterationSegments(string path, string expected)
    {
        Assert.Equal(expected, RuntimeReadNodeScope.WithoutIterationSegments(path));
    }

    [Fact]
    public async Task ExecutePipelineAsync_TwoExecutions_GetTwoExecutionRecords()
    {
        var capture = new ReadScopeCapture();
        var pipeline = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration> { new ReadScopeCaptureNodeConfiguration { Capture = capture } }
        };

        await ExecuteAsync(pipeline, NewContext());
        await ExecuteAsync(pipeline, NewContext());

        Assert.Equal(2, capture.Before.Count);
        var executions = capture.Before.Select(c => c.Execution).ToList();
        Assert.NotSame(executions[0], executions[1]);
    }

    private async Task ExecuteAsync(NodeDefinitionRoot pipeline, DefaultEtlContext context, JsonNode? value = null)
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        await orchestrator.ExecutePipelineAsync(pipeline, context, value: value);
    }

    private static DefaultEtlContext NewContext() =>
        new("read-scope-tenant", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
            new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId()), DateTime.UtcNow, null,
            new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>());
}

internal class ReadScopeCapture
{
    public ConcurrentQueue<RuntimeReadCaller> Before { get; } = new();
    public ConcurrentQueue<RuntimeReadCaller> After { get; } = new();
}

[NodeName("ReadScopeCapture", 1)]
internal record ReadScopeCaptureNodeConfiguration : NodeConfiguration
{
    public ReadScopeCapture Capture { get; set; } = new();
}

[NodeConfiguration(typeof(ReadScopeCaptureNodeConfiguration))]
internal class ReadScopeCaptureNode(NodeDelegate next) : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var cfg = nodeContext.GetNodeConfiguration<ReadScopeCaptureNodeConfiguration>();
        if (RuntimeReadScope.Current is { } before)
        {
            cfg.Capture.Before.Enqueue(before);
        }

        await next(dataContext, nodeContext);

        if (RuntimeReadScope.Current is { } after)
        {
            cfg.Capture.After.Enqueue(after);
        }
    }
}
