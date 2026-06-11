using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.Benchmarks;

/// <summary>
/// Memory benchmark mirroring the Phase 0 baseline (5 MB document, 1000-element iteration)
/// but driven through the post-migration STJ-based <see cref="IDataContext"/> surface
/// instead of Newtonsoft <c>JObject.DeepClone()</c>.
///
/// The Phase 0 measurement (Newtonsoft <c>DeepClone</c> per iteration) recorded
/// 924 MB allocated / 431 ms — see <c>docs/superpowers/plans/baseline-perf.txt</c>.
/// This benchmark records the new figure for direct comparison.
///
/// In addition to cumulative allocations, both methods now measure the peak managed
/// heap (max <see cref="GC.GetTotalMemory(bool)"/> sampled while the body runs). This
/// captures the actual *live* memory footprint, which is the metric that determines
/// "how much RAM does this pipeline need" — total allocations are invariant under
/// parallelism for a given workload, but peak heap is dramatically higher in parallel
/// when each iteration holds heavy intermediate state simultaneously.
/// </summary>
[Collection("AllocationGates")]
public class ForEachMemoryBenchmark : IClassFixture<NodeFixture>
{
    private readonly NodeFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ForEachMemoryBenchmark(NodeFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ForEach_SequentialMemoryFootprint()
    {
        // Same 5 MB document shape as the parallel benchmark — apples-to-apples harness:
        // both methods drive the real ForEachNode through the orchestrator and differ
        // ONLY in MaxDegreeOfParallelism (this one = 1, parallel one = -1).
        var arr = new JsonArray();
        for (var i = 0; i < 1000; i++)
        {
            arr.Add(new JsonObject
            {
                ["id"] = i,
                ["payload"] = new string('x', 5000)
            });
        }
        var fullDoc = new JsonObject
        {
            ["items"] = arr,
            ["bigBlob"] = new string('y', 1_000_000)
        };

        using var document = JsonDocument.Parse(fullDoc.ToJsonString());
        var dataContext = new DataContextImpl(document.RootElement);

        // Build a real ForEachNode pipeline with a single trivial child node so the
        // iteration walks the array end-to-end through the orchestrator code path —
        // identical setup to the parallel variant below; only MaxDoP differs.
        var testCounter = A.Fake<ITestCounter>();
        _fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var config = new ForEachNodeConfiguration
        {
            Path = "$.items",
            IterationPath = "$.items",
            TargetPath = "$.Result",
            // 1 = strictly sequential execution through the orchestrator.
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var serviceProvider = _fixture.Services.BuildServiceProvider();
        var logger = A.Fake<IPipelineLogger>();
        var rootNodeContext = NodeContext.CreateRootNodeContext(serviceProvider, logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, config, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        // Background sampler: poll GC.GetTotalMemory every ~5 ms, track the max.
        // Reported peak is a *lower bound* on the true peak (GC could collect between
        // samples), but for a 30-700 ms run the resolution is sufficient for the
        // sequential-vs-parallel comparison this benchmark is designed for.
        using var cts = new CancellationTokenSource();
        long peakBytes = 0;
        // xUnit1051 wants CancellationToken on Task.Run; we own this token's lifetime
        // here (the sampler is local infrastructure, not a unit-under-test op), so the
        // analyzer's preference doesn't apply. Suppress with #pragma to keep intent clear.
#pragma warning disable xUnit1051
        var samplerToken = cts.Token;
        var sampler = Task.Run(() =>
        {
            while (!samplerToken.IsCancellationRequested)
            {
                var current = GC.GetTotalMemory(forceFullCollection: false);
                var prev = Volatile.Read(ref peakBytes);
                while (current > prev)
                {
                    var actual = Interlocked.CompareExchange(ref peakBytes, current, prev);
                    if (actual == prev) break;
                    prev = actual;
                }
                Thread.Sleep(5);
            }
        }, samplerToken);
#pragma warning restore xUnit1051

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        sw.Stop();
        var deltaBytes = GC.GetTotalAllocatedBytes(precise: true) - beforeBytes;
        cts.Cancel();
        try { await sampler; } catch (TaskCanceledException) { /* expected */ }

        // All 1000 iterations should have produced an entry in $.Result.
        Assert.Equal(1000, dataContext.Length("$.Result"));

        _output.WriteLine(
            $"Sequential: {deltaBytes / (1024 * 1024)} MB allocated, " +
            $"peak {peakBytes / (1024 * 1024)} MB heap, {sw.ElapsedMilliseconds} ms " +
            $"(MaxDoP = 1, real ForEachNode + orchestrator)");

        // Zero-copy regression gate. The migration's reason for existing was to eliminate the
        // per-iteration full-document DeepClone (Newtonsoft baseline for this exact workload:
        // 924 MB allocated — see docs/superpowers/plans/baseline-perf.txt). The post-migration
        // figure is ~82 MB. The ceiling sits comfortably above the zero-copy actual yet far below
        // the clone-per-iteration regression scale, so reintroducing a full-template clone fails
        // the build instead of silently passing.
        const long maxAllocatedBytes = 300L * 1024 * 1024;
        Assert.True(deltaBytes < maxAllocatedBytes,
            $"ForEach (sequential) allocated {deltaBytes / (1024 * 1024)} MB, exceeding the " +
            $"{maxAllocatedBytes / (1024 * 1024)} MB zero-copy ceiling — a per-iteration deep clone " +
            "may have been reintroduced.");
    }

    /// <summary>
    /// Parallel variant of <see cref="ForEach_SequentialMemoryFootprint"/>. Drives the
    /// production <see cref="ForEachNode"/> with <c>MaxDegreeOfParallelism = -1</c>
    /// (unlimited) so iteration bodies execute concurrently — the same parallelism
    /// path that was silently dropped in Phase 4 and restored in commit <c>736b5ca</c>.
    /// Records elapsed time, allocated bytes, and peak managed heap for comparison
    /// against the single-thread post-migration figure recorded in
    /// <c>baseline-perf.txt</c>.
    /// </summary>
    [Fact]
    public async Task ForEach_ParallelMemoryFootprint()
    {
        // Same 5 MB document shape as the single-thread benchmark.
        var arr = new JsonArray();
        for (var i = 0; i < 1000; i++)
        {
            arr.Add(new JsonObject
            {
                ["id"] = i,
                ["payload"] = new string('x', 5000)
            });
        }
        var fullDoc = new JsonObject
        {
            ["items"] = arr,
            ["bigBlob"] = new string('y', 1_000_000)
        };

        using var document = JsonDocument.Parse(fullDoc.ToJsonString());
        var dataContext = new DataContextImpl(document.RootElement);

        // Build a real ForEachNode pipeline with a single trivial child node so the
        // iteration walks the array end-to-end through the parallel code path.
        // ITestCounter's GetNext() is the body; it returns a fresh int per call which
        // ForEachNode collects into a result array.
        var testCounter = A.Fake<ITestCounter>();
        _fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var config = new ForEachNodeConfiguration
        {
            Path = "$.items",
            IterationPath = "$.items",
            TargetPath = "$.Result",
            // -1 = unlimited parallelism (Parallel.ForAsync uses Environment.ProcessorCount).
            MaxDegreeOfParallelism = -1,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var serviceProvider = _fixture.Services.BuildServiceProvider();
        var logger = A.Fake<IPipelineLogger>();
        var rootNodeContext = NodeContext.CreateRootNodeContext(serviceProvider, logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, config, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        // Warm up the pipeline once on a tiny doc so first-call allocations (JIT,
        // ServiceProvider, etc.) are not charged to the measurement.
        // (We're measuring the iteration work, not the orchestrator startup cost.)

        // Background sampler: poll GC.GetTotalMemory every ~5 ms, track the max. Under
        // parallelism with N concurrent iterations, peak heap is the metric that
        // actually differs from the sequential run — total allocations are invariant.
        using var cts = new CancellationTokenSource();
        long peakBytes = 0;
        // xUnit1051 wants CancellationToken on Task.Run; we own this token's lifetime
        // here (the sampler is local infrastructure, not a unit-under-test op), so the
        // analyzer's preference doesn't apply. Suppress with #pragma to keep intent clear.
#pragma warning disable xUnit1051
        var samplerToken = cts.Token;
        var sampler = Task.Run(() =>
        {
            while (!samplerToken.IsCancellationRequested)
            {
                var current = GC.GetTotalMemory(forceFullCollection: false);
                var prev = Volatile.Read(ref peakBytes);
                while (current > prev)
                {
                    var actual = Interlocked.CompareExchange(ref peakBytes, current, prev);
                    if (actual == prev) break;
                    prev = actual;
                }
                Thread.Sleep(5);
            }
        }, samplerToken);
#pragma warning restore xUnit1051

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        sw.Stop();
        var deltaBytes = GC.GetTotalAllocatedBytes(precise: true) - beforeBytes;
        cts.Cancel();
        try { await sampler; } catch (TaskCanceledException) { /* expected */ }

        // All 1000 iterations should have produced an entry in $.Result.
        Assert.Equal(1000, dataContext.Length("$.Result"));

        _output.WriteLine(
            $"Parallel: {deltaBytes / (1024 * 1024)} MB allocated, " +
            $"peak {peakBytes / (1024 * 1024)} MB heap, {sw.ElapsedMilliseconds} ms " +
            $"(MaxDoP = unlimited, ProcessorCount = {Environment.ProcessorCount})");

        // Zero-copy regression gate — see ForEach_SequentialMemoryFootprint for rationale.
        // Under parallelism a reintroduced per-iteration clone is even worse (N concurrent copies),
        // so the same allocation ceiling guards this path.
        const long maxAllocatedBytes = 300L * 1024 * 1024;
        Assert.True(deltaBytes < maxAllocatedBytes,
            $"ForEach (parallel) allocated {deltaBytes / (1024 * 1024)} MB, exceeding the " +
            $"{maxAllocatedBytes / (1024 * 1024)} MB zero-copy ceiling — a per-iteration deep clone " +
            "may have been reintroduced.");
    }
}
