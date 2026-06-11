using System.Text.Json;
using FakeItEasy;
using LiteDB;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Load;

public class BufferRetrievalNodeTests : IDisposable
{
    private string? _tempStoragePath;

    private (IDataContext, INodeContext) PrepareDataContext()
    {
        BufferRetrievalNodeConfiguration r = new();
        var logger = A.Fake<IPipelineLogger>();

        // Pre-#3519 this used the shared NodeFixture.Services and mutated it in
        // place, which left two races behind: parallel test runs overwrote each
        // other's EdgeDataBufferConfiguration.StoragePath, and repeated
        // AddSingleton(IEdgeDataBuffer<>, …) calls accumulated duplicate
        // registrations. Both made BufferRetrievalNode read from someone else's
        // LiteDB file (or none). A per-test ServiceCollection removes both.
        var services = new ServiceCollection();
        BuidDi(services);

        var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("BufferRetrievalNode", 0, r, dataContext);

        return (dataContext, nodeContext);
    }

    private void BuidDi(ServiceCollection fixtureServices)
    {
        _tempStoragePath = Path.Combine(Path.GetTempPath(), "BufferRetrievalNodeTests_" + Guid.NewGuid().ToString("N"));
        // EdgeDataBuffer<> resolves ILoggerFactory; the old shared NodeFixture
        // inherited that from ServiceCollectionFixture. With per-test services we
        // recreate it here (plus AddOptions for the Configure call below).
        fixtureServices.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace));
        fixtureServices.AddOptions();
        fixtureServices.Configure<EdgeDataBufferConfiguration>(options =>
        {
            options.StoragePath = _tempStoragePath;
        });
        fixtureServices.AddSingleton(typeof(IEdgeDataBuffer<>), typeof(EdgeDataBuffer<>));
        fixtureServices.AddSingleton<ILiteDBFactory, LiteDbFileFactory>();
    }

    [Fact]
    public async Task ProcessObjectAsync_DataNotModified_OK()
    {
        var (dataContext, nodeContext) = PrepareDataContext();
        var dataBuffer = InsertTestData(nodeContext);

        var fn = A.Fake<NodeDelegate>();
        var retrievalNode = new BufferRetrievalNode(fn, dataBuffer);

        await retrievalNode.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(DataKind.Array, dataContext.GetKind("$"));
        Assert.Equal(5, dataContext.Length("$"));

        Assert.Equal(DataKind.Object, dataContext.GetKind("$[0]"));
        Assert.Equal(1, dataContext.Get<int>("$[0].a"));
        Assert.Equal(1, dataContext.Get<int>("$[0].b"));
        Assert.Equal(1, dataContext.Get<int>("$[0].c"));
    }

    // Regression guard for a close/retrieve race: AddDataPoint used to persist chunk
    // metadata via a fire-and-forget Task.Run, which could land after TryCloseCurrentChunk
    // and revert the chunk's persisted State from Closed back to Open — making the chunk
    // invisible to GetClosedChunks() and silently dropping its buffered data. Targets the
    // EdgeDataBuffer metadata lifecycle directly; loops to expose the timing window.
    [Fact]
    public void Buffer_CloseThenRetrieve_ChunkAlwaysVisible_Stress()
    {
        for (var i = 0; i < 200; i++)
        {
            var path = Path.Combine(Path.GetTempPath(), "BufStress_" + Guid.NewGuid().ToString("N"));
            var buffer = new EdgeDataBuffer<Dictionary<string, BsonValue>>(
                NullLoggerFactory.Instance,
                new LiteDbFileFactory(),
                Options.Create(new EdgeDataBufferConfiguration { StoragePath = path }));
            try
            {
                var chunk = buffer.GetOrCreateOpenChunk();
                for (var j = 0; j < 5; j++)
                {
                    chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(
                        new Dictionary<string, BsonValue> { ["a"] = j }));
                }

                buffer.TryCloseCurrentChunk(true);

                var closed = buffer.GetClosedChunks().ToList();
                Assert.True(closed.Count == 1, $"iteration {i}: expected 1 closed chunk, got {closed.Count}");
                var points = closed[0].GetDataPoints().ToList();
                Assert.True(points.Count == 5, $"iteration {i}: expected 5 datapoints, got {points.Count}");
                foreach (var c in closed) c.Dispose();
            }
            finally
            {
                buffer.Dispose();
                try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { /* best effort */ }
            }
        }
    }

    private static IEdgeDataBuffer<Dictionary<string, BsonValue>> InsertTestData(INodeContext nodeContext)
    {
        var dataBuffer = nodeContext.ServiceProvider
            .GetRequiredService<IEdgeDataBuffer<Dictionary<string, BsonValue>>>();

        var chunk = dataBuffer.GetOrCreateOpenChunk();
        chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(new Dictionary<string, BsonValue>()
        {
            ["a"] = 1,
            ["b"] = 1,
            ["c"] = 1
        }));
        chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(new Dictionary<string, BsonValue>()
        {
            ["a"] = 2,
            ["c"] = 2
        }));
        chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(new Dictionary<string, BsonValue>()
        {
            ["a"] = 3,
            ["b"] = 3,
        }));
        chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(new Dictionary<string, BsonValue>()
        {
            ["b"] = 4,
            ["c"] = 4,
        }));
        chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(new Dictionary<string, BsonValue>()
        {
            ["x"] = 5,
            ["y"] = 5,
            ["z"] = 5
        }));

        dataBuffer.TryCloseCurrentChunk(true);
        return dataBuffer;
    }

    public void Dispose()
    {
        if (_tempStoragePath != null && Directory.Exists(_tempStoragePath))
        {
            try
            {
                Directory.Delete(_tempStoragePath, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
