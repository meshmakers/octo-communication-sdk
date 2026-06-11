using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

public class ForEachNodeTests(NodeFixture fixture, ITestOutputHelper testOutputHelper)
    : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(ForEachNodeConfiguration forEachNodeConfiguration,
        IPipelineDebugger? debugger = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var json = JsonSerializer.Serialize(Generator.GenerateOrder(), SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_OK()
    {
        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SourceNull_Fail()
    {
        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$.ItemsNotExisting",
            IterationPath = "$.ItemsNotExisting",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
            await testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NotAnArray_Fail()
    {
        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$.ItemsNotExisting",
            IterationPath = "$.ItemsNotExisting",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
            await testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_Output_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Output0"
                },
                new TestNodeConfiguration
                {
                    TargetPath = "$.Output1"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());
        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());

        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);
        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration, debugger);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var debugInfo = debugger.GetDebugInformation();
        Assert.NotNull(debugInfo);
    }

    [Fact]
    public async Task ProcessObjectAsync_Debugger_IterationChildSnapshot_ContainsFullAlias()
    {
        // Regression guard (AB#3517): the pipeline debugger captures every node's input/output
        // via dataContext.Get<JsonNode>("$"), which routes through the alias-omitting
        // IReadSource.TryGetNode. For nodes INSIDE a ForEach, "$.full" is a synthetic alias
        // layered on the iteration child (not part of its overlay), so TryGetNode drops it and
        // the debug UI shows the per-item data but is MISSING "full". Pre-STJ-migration the
        // debugger captured dataContext.Current — a JObject that physically carried "full" —
        // so the debug views showed it. The captured snapshot of an in-loop node must therefore
        // still carry the "$.full" alias.
        fixture.UseXUnitLoggerFactory(testOutputHelper);

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$", // $.full aliases to the whole document
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1, // deterministic ordering for the assertion
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.key" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        // Build the provider AFTER registering the test counter so the child TestNode resolves.
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());
        debugger.RegisterPipelineRtEntityId(
            new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId()), Guid.NewGuid());

        // Wire the debugger into the ROOT context so child node registrations are captured
        // (the shared PrepareTest helper does not forward its debugger argument).
        var logger = A.Fake<IPipelineLogger>();
        var json = JsonSerializer.Serialize(Generator.GenerateOrder(), SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(serviceProvider, logger, dataContext, debugger);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var debugInfo = debugger.GetDebugInformation();

        // The iteration children are the debug points whose captured input is the per-item
        // sub-context (seeded under "$.key"). Each such snapshot must also carry "$.full".
        var iterationChildInputs = debugInfo.DebugPoints
            .Where(dp => dp.Input is not null)
            .Select(dp => JsonNode.Parse(dp.Input!)!.AsObject())
            .Where(o => o.ContainsKey("key"))
            .ToList();

        Assert.NotEmpty(iterationChildInputs);
        Assert.All(iterationChildInputs, o =>
            Assert.True(o.ContainsKey("full"),
                $"Iteration-child debug snapshot must carry the $.full alias; got: {o.ToJsonString()}"));
    }

    [Fact]
    public async Task ProcessObjectAsync_IterationPath_WithReplace_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            IterationPath = "$.Items",
            DocumentMode = DocumentModes.Replace,
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.key.Output0"
                },
                new TestNodeConfiguration
                {
                    TargetPath = "$.key.Output1"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).ReturnsNextFromSequence(0, 1, 2, 3, 4, 5);

        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());
        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());

        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);
        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration, debugger);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(3, dataContext.Length("$.Result"));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(dataContext.Exists($"$.Result[{i}].Output0"));
            Assert.True(dataContext.Exists($"$.Result[{i}].Output1"));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task ProcessObjectAsync_WithMaxDegreeOfParallelism_AllItemsProcessed(int maxDop)
    {
        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = maxDop,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MaxDegreeOfParallelism_LimitsConcurrency()
    {
        var concurrencyTracker = new ConcurrencyTracker();
        fixture.Services.AddSingleton(concurrencyTracker);

        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                new DelayedTestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(1, concurrencyTracker.MaxConcurrent);
        Assert.Equal(3, concurrencyTracker.TotalExecutions);
        Assert.Equal(3, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_RunsInParallel_WhenConfigured()
    {
        // Build a JSON document with 50 items so iteration body has enough work to
        // demonstrate parallelism. Each iteration calls DelayedTestNode (Task.Delay(50ms)).
        // Sequential lower bound: 50 * 50ms = 2500ms.
        // With MaxDegreeOfParallelism = -1 (unlimited), elapsed time should be well under
        // half the sequential bound — we assert < 1500ms which leaves wide headroom while
        // still failing if the implementation reverted to sequential execution.
        const int itemCount = 50;
        var items = new JsonArray();
        for (var i = 0; i < itemCount; i++) items.Add(new JsonObject { ["Id"] = i });
        var rootJson = new JsonObject { ["Items"] = items }.ToJsonString();

        var concurrencyTracker = new ConcurrencyTracker();
        fixture.Services.AddSingleton(concurrencyTracker);

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = -1, // unlimited
            Transformations = new List<NodeConfiguration>
            {
                new DelayedTestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        var stopwatch = Stopwatch.StartNew();
        await testee.ProcessObjectAsync(dataContext, nodeContext);
        stopwatch.Stop();

        // All items processed
        Assert.Equal(itemCount, concurrencyTracker.TotalExecutions);
        Assert.Equal(itemCount, dataContext.Length("$.Result"));

        // At least 2 iterations ran concurrently — otherwise the implementation went
        // sequential (the regression we're guarding against).
        Assert.True(concurrencyTracker.MaxConcurrent >= 2,
            $"Expected concurrent execution, but observed MaxConcurrent={concurrencyTracker.MaxConcurrent}");

        // Wall-clock check as a secondary guarantee: a sequential implementation would take
        // at least 50 * 50ms = 2500ms. A parallel implementation finishes far sooner.
        Assert.True(stopwatch.ElapsedMilliseconds < 1500,
            $"Expected parallel execution to complete in under 1500ms, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ForEachNode_SeedsKeyPathWithCurrentItem()
    {
        // Build a JSON document with three items {Id:1}, {Id:2}, {Id:3}.
        // The child transformation reads $.key.Id (via FullDocAccessTestNode) and
        // writes to $.key.captured. Default MergePath = $.key, so the merged result
        // array element for each iteration is the rewritten $.key. If the iteration
        // item wasn't seeded under $.key, FullDocAccessTestNode would read null and
        // the assertion below would fail.
        var items = new JsonArray(
            new JsonObject { ["Id"] = 1 },
            new JsonObject { ["Id"] = 2 },
            new JsonObject { ["Id"] = 3 });
        var rootJson = new JsonObject { ["Items"] = items }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                new FullDocAccessTestNodeConfiguration
                {
                    SourcePath = "$.key.Id",
                    TargetPath = "$.key.captured",
                }
            }
        };

        fixture.Services.AddSingleton<IFullDocAccessResult>(new FullDocAccessResult());

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(3, dataContext.Length("$.Result"));
        // Each result element is the merged $.key — verify $.key.captured holds the Id.
        var captured = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(dataContext.Exists($"$.Result[{i}].captured"),
                $"$.Result[{i}].captured should exist (item was seeded under $.key)");
            captured.Add(dataContext.Get<int>($"$.Result[{i}].captured"));
        }
        Assert.Equal(new[] { 1, 2, 3 }, captured.OrderBy(x => x));
    }

    // ---- Merge-path tests ----------------------------------------------------------
    // The "merge path" is ForEachNode's per-iteration result collection mechanism:
    // after each iteration body, the value at c.MergePath is deep-cloned into a
    // ConcurrentBag, then all collected items are deep-cloned again into a JsonArray
    // and written to c.TargetPath via c.TargetValueWriteMode. The two DeepClone calls
    // and the null-skip are the STJ-sensitive bits — these tests pin that behavior.

    [Fact]
    public async Task ProcessObjectAsync_CustomMergePath_CollectsFromComputedLocation()
    {
        // MergePath defaults to $.key (the iteration item). Override it to point at a
        // path the child transformation writes, and verify the merged result reflects
        // the *computed* values, not the raw iteration items.
        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MergePath = "$.computed",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.computed" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).ReturnsNextFromSequence(10, 20, 30);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(3, dataContext.Length("$.Result"));
        var values = new List<int>();
        for (var i = 0; i < 3; i++) values.Add(dataContext.Get<int>($"$.Result[{i}]"));
        Assert.Equal(new[] { 10, 20, 30 }, values.OrderBy(v => v));
    }

    [Fact]
    public async Task ProcessObjectAsync_MergePathReturnsNull_SkipsItem()
    {
        // ForEachNode.cs:171-172 — `if (mergeItem is not null) collected.Add(...)`.
        // When MergePath points at a path no child transformation ever writes, every
        // iteration is skipped and the result array is empty (but next() still runs).
        var items = new JsonArray(
            new JsonObject { ["Id"] = 1 },
            new JsonObject { ["Id"] = 2 },
            new JsonObject { ["Id"] = 3 });
        var rootJson = new JsonObject { ["Items"] = items }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MergePath = "$.never_written",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                // Writes $.key (default seed location) — does NOT write $.never_written.
                new TestNodeConfiguration { TargetPath = "$.key" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Three iterations ran (counter called 3x), but nothing was merged.
        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(3, Times.Exactly);
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.Result"));
        Assert.Equal(0, dataContext.Length("$.Result"));
        // next() runs exactly once even when nothing was collected.
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_MergePathReturnsComplexObject_PreservesAllProperties()
    {
        // Default MergePath = $.key collects the iteration item itself. With a complex
        // multi-property item, every property must survive the two DeepClone hops
        // (per-iteration bag insert + final resultArray build). Guards the STJ
        // DeepClone fidelity on nested JsonObject values.
        var items = new JsonArray(
            new JsonObject
            {
                ["Id"] = 1,
                ["Name"] = "alpha",
                ["Nested"] = new JsonObject { ["Inner"] = 100, ["Tag"] = "x" }
            },
            new JsonObject
            {
                ["Id"] = 2,
                ["Name"] = "beta",
                ["Nested"] = new JsonObject { ["Inner"] = 200, ["Tag"] = "y" }
            });
        var rootJson = new JsonObject { ["Items"] = items }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1,
            // No transformations — just verify the seeded $.key item gets merged intact.
            Transformations = new List<NodeConfiguration>()
        };

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(2, dataContext.Length("$.Result"));
        // Sort by Id so the parallel-merge ordering doesn't matter (MaxDoP=1 here,
        // but the same assertion shape will hold under any DoP).
        var observed = new List<(int Id, string? Name, int Inner, string? Tag)>();
        for (var i = 0; i < 2; i++)
        {
            observed.Add((
                dataContext.Get<int>($"$.Result[{i}].Id"),
                dataContext.Get<string>($"$.Result[{i}].Name"),
                dataContext.Get<int>($"$.Result[{i}].Nested.Inner"),
                dataContext.Get<string>($"$.Result[{i}].Nested.Tag")));
        }
        var sorted = observed.OrderBy(t => t.Id).ToList();
        Assert.Equal((1, (string?)"alpha", 100, (string?)"x"), sorted[0]);
        Assert.Equal((2, (string?)"beta", 200, (string?)"y"), sorted[1]);
    }

    [Fact]
    public async Task ProcessObjectAsync_MergeResultIsIsolatedFromSourceArray()
    {
        // DeepClone-isolation invariant: mutating the merged Result must not bleed
        // back into the source Items array. ForEachNode.cs:135 clones the item before
        // seeding the child context; :172 clones again into the bag; :142 clones a
        // third time into the result array. Together they guarantee Result and Items
        // share no JsonNode references.
        var items = new JsonArray(
            new JsonObject { ["n"] = 1 },
            new JsonObject { ["n"] = 2 });
        var rootJson = new JsonObject { ["Items"] = items }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>()
        };

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Overwrite Result[0].n via the public Set API — this mutates the overlay's
        // stored JsonNode at $.Result[0].
        dataContext.Set("$.Result[0].n", 999);
        Assert.Equal(999, dataContext.Get<int>("$.Result[0].n"));

        // Source Items[0] must still see its original value (no aliasing).
        Assert.Equal(1, dataContext.Get<int>("$.Items[0].n"));
        Assert.Equal(2, dataContext.Get<int>("$.Items[1].n"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyIterationArray_ProducesEmptyResultAndCallsNext()
    {
        // Early-return path at ForEachNode.cs:76-82 — empty source array still writes
        // an empty JsonArray to TargetPath and invokes next(). Guards against either
        // (a) skipping the TargetPath write or (b) skipping the next() invocation.
        var rootJson = new JsonObject { ["Items"] = new JsonArray() }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.key" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // No iteration ran — child transformation was never invoked.
        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        // Result is an empty array, not absent.
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.Result"));
        Assert.Equal(0, dataContext.Length("$.Result"));
        // next() still ran exactly once.
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_AppendWriteMode_AppendsResultArrayToExistingTargetArray()
    {
        // The merge result is written via dataContext.Set with the configured
        // TargetValueWriteMode (ForEachNode.cs:143). When Append is configured and
        // TargetPath already holds an array, each element of the result array is
        // appended to it (see DataContext.AppendOrPrependCore — array operand spreads
        // its elements into the existing array).
        var items = new JsonArray(
            new JsonObject { ["Id"] = 10 },
            new JsonObject { ["Id"] = 20 });
        var rootJson = new JsonObject
        {
            ["Items"] = items,
            ["Result"] = new JsonArray(new JsonObject { ["Id"] = 99 }),
        }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            TargetValueWriteMode = TargetValueWriteModes.Append,
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>()
        };

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Pre-seeded element (Id=99) + two merged items = 3 elements total.
        Assert.Equal(3, dataContext.Length("$.Result"));
        // The pre-seeded element remains at index 0 (Append never reorders).
        Assert.Equal(99, dataContext.Get<int>("$.Result[0].Id"));

        var appendedIds = new List<int>
        {
            dataContext.Get<int>("$.Result[1].Id"),
            dataContext.Get<int>("$.Result[2].Id"),
        };
        Assert.Equal(new[] { 10, 20 }, appendedIds.OrderBy(v => v));
    }

    [Fact]
    public async Task ProcessObjectAsync_HighParallelism_AllMergedItemsObserved()
    {
        // Even under unlimited parallelism with many items, every iteration must
        // contribute exactly one element to the merged result (ConcurrentBag is
        // thread-safe; no item drops, no duplicates). Mirrors the contract checked
        // by Sdk.Common.PipelineParityTests.IterationParityTests but exercises the
        // real ForEachNode rather than a harness.
        const int itemCount = 200;
        var items = new JsonArray();
        for (var i = 0; i < itemCount; i++) items.Add(new JsonObject { ["Id"] = i });
        var rootJson = new JsonObject { ["Items"] = items }.ToJsonString();

        var forEachNodeConfiguration = new ForEachNodeConfiguration
        {
            Path = "$.Items",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = -1,
            Transformations = new List<NodeConfiguration>()
        };

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(rootJson));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, forEachNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(itemCount, dataContext.Length("$.Result"));
        // Every input Id appears exactly once (no loss, no duplication).
        var observed = new List<int>(itemCount);
        for (var i = 0; i < itemCount; i++) observed.Add(dataContext.Get<int>($"$.Result[{i}].Id"));
        observed.Sort();
        for (var i = 0; i < itemCount; i++) Assert.Equal(i, observed[i]);
    }

    // Phase 11 regression: ForEachNode with Path="$" + IterationPath="$.Items" doesn't
    // produce the same shared-data accessible-via-Current behavior as the legacy DataContext.
    // The legacy test specifically reproduced the bug that real nodes (FormatStringNode etc.)
    // accessed "$.full.X" via Current.SelectToken; the new path-only API has no Current and
    // the equivalent semantic (FullDocAccessTestNode reads "$.full.InvoiceNumber") returns
    // empty result set. Needs investigation of ForEachNode shared-data exposure.
    [Fact]
    public async Task ProcessObjectAsync_FullDocumentPath_AccessibleViaCurrentSelectToken()
    {
        ForEachNodeConfiguration forEachNodeConfiguration = new()
        {
            Path = "$",
            IterationPath = "$.Items",
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                new FullDocAccessTestNodeConfiguration
                {
                    SourcePath = "$.full.InvoiceNumber",
                    TargetPath = "$.key",
                }
            }
        };

        fixture.Services.AddSingleton<IFullDocAccessResult>(new FullDocAccessResult());

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForEachNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(3, dataContext.Length("$.Result"));

        var invoiceNumber = dataContext.Get<int>("$.InvoiceNumber");
        for (var i = 0; i < 3; i++)
        {
            // The result element must be a number equal to the actual InvoiceNumber, not null.
            Assert.Equal(DataKind.Number, dataContext.GetKind($"$.Result[{i}]"));
            Assert.Equal(invoiceNumber, dataContext.Get<int>($"$.Result[{i}]"));
        }
    }
}
