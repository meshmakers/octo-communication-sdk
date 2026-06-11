using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Test configuration for the concrete ObjectIteratorNode implementation
/// </summary>
[NodeName("TestObjectIterator", 1)]
public record TestObjectIteratorNodeConfiguration
    : ObjectIteratorNodeConfiguration<TokenConfigurationNode>, IChildNodeConfiguration
{
    public ICollection<NodeConfiguration>? Transformations { get; set; }
}

/// <summary>
/// Concrete test implementation of the abstract ObjectIteratorNode
/// </summary>
[NodeConfiguration(typeof(TestObjectIteratorNodeConfiguration))]
public class TestObjectIteratorNode(NodeDelegate next) : ObjectIteratorNode<TokenConfigurationNode>
{
    public override async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<TestObjectIteratorNodeConfiguration>();

        foreach (var selectPathItem in c.SelectPath)
        {
            await ProcessToken(dataContext, nodeContext, next, selectPathItem);
        }
    }
}

public class ObjectIteratorNodeTests : IClassFixture<NodeFixture>
{
    private readonly NodeFixture _fixture;

    public ObjectIteratorNodeTests(NodeFixture fixture)
    {
        _fixture = fixture;
        _fixture.RegisterNode(typeof(TestObjectIteratorNode));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithArray_ProcessesAllElements()
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse("[1, 2, 3]"));
        var config = new TestObjectIteratorNodeConfiguration
        {
            SelectPath = new List<TokenConfigurationNode>
            {
                new() { Transformations = null }
            }
        };

        var (nodeContext, fn) = PrepareTest(config, dataContext);
        var testee = new TestObjectIteratorNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(DataKind.Array, dataContext.GetKind("$"));
        Assert.Equal(3, dataContext.Length("$"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithSingleObject_ProcessesSingleElement()
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"key\":\"value\"}"));
        var config = new TestObjectIteratorNodeConfiguration
        {
            SelectPath = new List<TokenConfigurationNode>
            {
                new() { Transformations = null }
            }
        };

        var (nodeContext, fn) = PrepareTest(config, dataContext);
        var testee = new TestObjectIteratorNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(DataKind.Object, dataContext.GetKind("$"));
        A.CallTo(() => fn.Invoke(A<IDataContext>._, A<INodeContext>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithEmptyArray_CallsNextWithEmptyArray()
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse("[]"));
        var config = new TestObjectIteratorNodeConfiguration
        {
            SelectPath = new List<TokenConfigurationNode>
            {
                new() { Transformations = null }
            }
        };

        var (nodeContext, fn) = PrepareTest(config, dataContext);
        var testee = new TestObjectIteratorNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(DataKind.Array, dataContext.GetKind("$"));
        Assert.Equal(0, dataContext.Length("$"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithObjectArray_PreservesObjectData()
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse(
            "[{\"name\":\"Alice\",\"age\":30},{\"name\":\"Bob\",\"age\":25}]"));
        var config = new TestObjectIteratorNodeConfiguration
        {
            SelectPath = new List<TokenConfigurationNode>
            {
                new() { Transformations = null }
            }
        };

        var (nodeContext, fn) = PrepareTest(config, dataContext);
        var testee = new TestObjectIteratorNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(DataKind.Array, dataContext.GetKind("$"));
        Assert.Equal(2, dataContext.Length("$"));
    }

    // ---- Merge-path tests ----------------------------------------------------------
    // ObjectIteratorNode's merge path is hardcoded to "$" (the root of each
    // iteration's child context). After each iteration body, the value at "$" is
    // deep-cloned into a ConcurrentBag, then all collected items are deep-cloned
    // again into a JsonArray and written back to the parent's "$"
    // (ObjectIteratorNode.cs:91-93,121-126). These tests pin the null-skip and
    // DeepClone-isolation contracts.

    [Fact]
    public async Task ProcessObjectAsync_ArrayWithNullElements_SkipsNullsInMergedResult()
    {
        // ObjectIteratorNode.cs:85 — `item = sourceArray[i]?.DeepClone()` — yields
        // null for null source elements. The child context is built with null at "$",
        // and the merge collector at :124-125 (`if (node is not null) collected.Add(...)`)
        // skips those iterations. Result must contain only non-null elements.
        var dataContext = new DataContextImpl(JsonDocument.Parse("[1, null, 3, null, 5]"));
        var config = new TestObjectIteratorNodeConfiguration
        {
            SelectPath = new List<TokenConfigurationNode>
            {
                new() { Transformations = null }
            }
        };

        var (nodeContext, fn) = PrepareTest(config, dataContext);
        var testee = new TestObjectIteratorNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(DataKind.Array, dataContext.GetKind("$"));
        Assert.Equal(3, dataContext.Length("$"));
        var observed = new List<int>
        {
            dataContext.Get<int>("$[0]"),
            dataContext.Get<int>("$[1]"),
            dataContext.Get<int>("$[2]"),
        };
        Assert.Equal(new[] { 1, 3, 5 }, observed.OrderBy(v => v));
    }

    [Fact]
    public async Task ProcessObjectAsync_MergeResultIsIsolatedFromSourceArray()
    {
        // DeepClone-isolation invariant for ObjectIteratorNode: the merged result
        // must share no JsonNode references with the source. Mutating the merged
        // result after iteration must not bleed back into source elements.
        // ObjectIteratorNode.cs:85 (item clone), :125 (bag-insert clone), :92
        // (resultArray clone) together provide the guarantee.
        var dataContext = new DataContextImpl(JsonDocument.Parse(
            "[{\"n\":1},{\"n\":2}]"));
        var config = new TestObjectIteratorNodeConfiguration
        {
            SelectPath = new List<TokenConfigurationNode>
            {
                new() { Transformations = null }
            }
        };

        var (nodeContext, fn) = PrepareTest(config, dataContext);
        var testee = new TestObjectIteratorNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Snapshot the merged values before mutation (parallel-merge order is
        // non-deterministic, so we capture both then sort).
        var beforeMutation = new List<int>
        {
            dataContext.Get<int>("$[0].n"),
            dataContext.Get<int>("$[1].n"),
        }.OrderBy(v => v).ToList();
        Assert.Equal(new[] { 1, 2 }, beforeMutation);

        // Mutate $[0].n via the public Set API. If the merged JsonArray aliased the
        // source it would be visible elsewhere — but ObjectIteratorNode wrote the
        // merge result back at "$", overwriting the source array. The remaining
        // value to check is that mutation through one index doesn't ghost-modify
        // the other (would indicate two indices share a JsonNode reference, which
        // would happen if either DeepClone hop were dropped).
        dataContext.Set("$[0].n", 999);

        Assert.Equal(999, dataContext.Get<int>("$[0].n"));
        // The other element must keep its original value — if the two result
        // elements shared a reference, this would have flipped to 999 too.
        var other = dataContext.Get<int>("$[1].n");
        Assert.True(other == 1 || other == 2,
            $"Sibling element corrupted by mutation: expected 1 or 2, got {other}");
        Assert.NotEqual(999, other);
    }

    private (INodeContext, NodeDelegate) PrepareTest(TestObjectIteratorNodeConfiguration config,
        IDataContext dataContext)
    {
        var logger = A.Fake<IPipelineLogger>();
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(_fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext =
            rootNodeContext.RegisterChildNode("TestObjectIterator", 0, config, dataContext);

        var fn = A.Fake<NodeDelegate>();

        return (nodeContext, fn);
    }
}
