using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using LiteDB;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Load;

public class BufferNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    [Fact]
    public async Task ProcessObjectAsync_StoresDataInBuffer_CallsNext()
    {
        var chunk = A.Fake<IChunkedDataBuffer<Dictionary<string, BsonValue>>>();
        var buffer = A.Fake<IEdgeDataBuffer<Dictionary<string, BsonValue>>>();
        A.CallTo(() => buffer.GetOrCreateOpenChunk()).Returns(chunk);

        var etlContext = CreateEtlContext();
        var orchestrator = A.Fake<IEtlDataOrchestrator>();
        var scheduler = A.Fake<IBufferScheduler>();
        fixture.Services.AddSingleton(scheduler);

        var config = new BufferNodeConfiguration
        {
            Path = "$",
            BufferTime = "00:05:00"
        };

        var testData = new JsonObject
        {
            ["temperature"] = 42.5,
            ["sensor"] = "T1"
        };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var testee = new BufferNode(fn, buffer, etlContext, orchestrator);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => buffer.GetOrCreateOpenChunk()).MustHaveHappenedOnceExactly();
        A.CallTo(() => chunk.AddDataPoint(A<DataPoint<Dictionary<string, BsonValue>>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_ClearsCurrentAfterBuffering()
    {
        var chunk = A.Fake<IChunkedDataBuffer<Dictionary<string, BsonValue>>>();
        var buffer = A.Fake<IEdgeDataBuffer<Dictionary<string, BsonValue>>>();
        A.CallTo(() => buffer.GetOrCreateOpenChunk()).Returns(chunk);

        var etlContext = CreateEtlContext();
        var orchestrator = A.Fake<IEtlDataOrchestrator>();
        var scheduler = A.Fake<IBufferScheduler>();
        fixture.Services.AddSingleton(scheduler);

        var config = new BufferNodeConfiguration
        {
            Path = "$",
            BufferTime = "00:01:00"
        };

        var testData = new JsonObject { ["value"] = 100 };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var testee = new BufferNode(fn, buffer, etlContext, orchestrator);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // After buffering, root should be an empty object
        Assert.Equal(DataKind.Object, dataContext.GetKind("$"));
        Assert.Empty(dataContext.Keys("$"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SchedulesBufferFlush_WhenFirstRun()
    {
        var chunk = A.Fake<IChunkedDataBuffer<Dictionary<string, BsonValue>>>();
        var buffer = A.Fake<IEdgeDataBuffer<Dictionary<string, BsonValue>>>();
        A.CallTo(() => buffer.GetOrCreateOpenChunk()).Returns(chunk);

        var etlContext = CreateEtlContext();
        var orchestrator = A.Fake<IEtlDataOrchestrator>();
        var scheduler = A.Fake<IBufferScheduler>();

        fixture.Services.AddSingleton(scheduler);

        var config = new BufferNodeConfiguration
        {
            Path = "$",
            BufferTime = "00:10:00"
        };

        var (dataContext, nodeContext) = PrepareTest(config, new JsonObject { ["v"] = 1 });

        var fn = A.Fake<NodeDelegate>();
        var testee = new BufferNode(fn, buffer, etlContext, orchestrator);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => scheduler.ScheduleOrReplace(A<Func<Task>>._, TimeSpan.FromMinutes(10)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithInvalidBufferTime_DefaultsTo10Seconds()
    {
        var chunk = A.Fake<IChunkedDataBuffer<Dictionary<string, BsonValue>>>();
        var buffer = A.Fake<IEdgeDataBuffer<Dictionary<string, BsonValue>>>();
        A.CallTo(() => buffer.GetOrCreateOpenChunk()).Returns(chunk);

        var etlContext = CreateEtlContext();
        var orchestrator = A.Fake<IEtlDataOrchestrator>();
        var scheduler = A.Fake<IBufferScheduler>();

        fixture.Services.AddSingleton(scheduler);

        var config = new BufferNodeConfiguration
        {
            Path = "$",
            BufferTime = "invalid-time"
        };

        var (dataContext, nodeContext) = PrepareTest(config, new JsonObject { ["v"] = 1 });

        var fn = A.Fake<NodeDelegate>();
        var testee = new BufferNode(fn, buffer, etlContext, orchestrator);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => scheduler.ScheduleOrReplace(A<Func<Task>>._, TimeSpan.FromSeconds(10)))
            .MustHaveHappenedOnceExactly();
    }

    private (IDataContext, INodeContext) PrepareTest(BufferNodeConfiguration config, JsonNode data)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(data.ToJsonString()));

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("BufferData", 0, config, dataContext);

        return (dataContext, nodeContext);
    }

    private static IEtlContext CreateEtlContext()
    {
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.Properties).Returns(new Dictionary<string, object?>());
        return etlContext;
    }
}
