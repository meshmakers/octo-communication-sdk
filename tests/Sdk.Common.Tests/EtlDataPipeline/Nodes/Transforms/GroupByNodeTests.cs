using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class GroupByNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private readonly IPipelineLogger _logger = A.Fake<IPipelineLogger>();

    private (IDataContext, INodeContext) Prepare(string json, NodeConfiguration config)
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), _logger, dataContext);
        return (dataContext, root.RegisterChildNode("GroupBy", 0, config, dataContext));
    }

    private async Task<(IDataContext Ctx, NodeDelegate Next, INodeContext NodeCtx)> RunAsync(
        string json, GroupByNodeConfiguration config)
    {
        var (dataContext, nodeContext) = Prepare(json, config);
        var fn = A.Fake<NodeDelegate>();
        await new GroupByNode(fn).ProcessObjectAsync(dataContext, nodeContext);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        return (dataContext, fn, nodeContext);
    }

    private static GroupByNodeConfiguration Config(string? keyPath = "$.k") =>
        new() { Path = "$.items", KeyPath = keyPath, TargetPath = "$.groups" };

    [Fact]
    public async Task GroupsByKey_InFirstOccurrenceOrder_ItemsKeepOrder()
    {
        const string json = """
        { "items": [
            { "k": "b", "n": 1 }, { "k": "a", "n": 2 }, { "k": "b", "n": 3 },
            { "k": "c", "n": 4 }, { "k": "a", "n": 5 }, { "k": "b", "n": 6 } ] }
        """;

        var (ctx, _, _) = await RunAsync(json, Config());

        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Equal(new[] { "b", "a", "c" }, groups.Select(g => g!["key"]!.GetValue<string>()));
        Assert.Equal(new[] { 1, 3, 6 }, groups[0]!["items"]!.AsArray().Select(i => i!["n"]!.GetValue<int>()));
        Assert.Equal(new[] { 2, 5 }, groups[1]!["items"]!.AsArray().Select(i => i!["n"]!.GetValue<int>()));
        Assert.Equal(new[] { 4 }, groups[2]!["items"]!.AsArray().Select(i => i!["n"]!.GetValue<int>()));
    }

    [Fact]
    public async Task NumericKeys_AreComparedByValue_OneAndOnePointZeroAreEqual()
    {
        const string json = """{ "items": [ { "k": 1 }, { "k": 2 }, { "k": 1.0 }, { "k": 2 } ] }""";

        var (ctx, _, _) = await RunAsync(json, Config());

        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0]!["items"]!.AsArray().Count);
        Assert.Equal(2, groups[1]!["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task StringAndNumberKeys_AreDistinct()
    {
        const string json = """{ "items": [ { "k": 1 }, { "k": "1" }, { "k": true }, { "k": "A" }, { "k": "a" } ] }""";

        var (ctx, _, _) = await RunAsync(json, Config());

        Assert.Equal(5, ctx.Get<JsonArray>("$.groups")!.Count);
    }

    [Fact]
    public async Task DateLikeStrings_StayDistinctStrings()
    {
        const string json = """{ "items": [ { "k": "2024-01-01" }, { "k": "2024-01-01T00:00:00" }, { "k": "2024-01-01" } ] }""";

        var (ctx, _, _) = await RunAsync(json, Config());

        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0]!["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task MissingAndNullKeys_AreSkipped_WithOneWarning()
    {
        const string json = """
        { "items": [ { "k": "a" }, { "x": 1 }, { "k": null }, { "k": "a" }, { "k": { "o": 1 } }, 5, { "k": "b" } ] }
        """;

        var (ctx, _, _) = await RunAsync(json, Config());

        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0]!["items"]!.AsArray().Count);
        A.CallTo(_logger).Where(c => c.Method.Name == nameof(IPipelineLogger.Warning))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task NoSkippedItems_NoWarning()
    {
        var (_, _, _) = await RunAsync("""{ "items": [ { "k": "a" } ] }""", Config());

        A.CallTo(_logger).Where(c => c.Method.Name == nameof(IPipelineLogger.Warning)).MustNotHaveHappened();
    }

    [Fact]
    public async Task EmptyArray_DoesNotWrite_AndContinues()
    {
        var (ctx, _, _) = await RunAsync("""{ "items": [] }""", Config());

        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.groups"));
    }

    [Theory]
    [InlineData("""{ "items": "text" }""")]
    [InlineData("""{ "items": { "k": "a" } }""")]
    [InlineData("""{ "other": 1 }""")]
    public async Task NonArrayOrMissingSource_DoesNotWrite_AndContinues(string json)
    {
        var (ctx, _, _) = await RunAsync(json, Config());

        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.groups"));
    }

    [Fact]
    public async Task ScalarArray_WithoutKeyPath_GroupsByValue()
    {
        var (ctx, _, _) = await RunAsync("""{ "items": [ "x", "y", "x", 3, 3 ] }""", Config(null));

        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Equal(3, groups.Count);
        Assert.Equal("x", groups[0]!["key"]!.GetValue<string>());
        Assert.Equal(2, groups[0]!["items"]!.AsArray().Count);
        Assert.Equal(3, groups[2]!["key"]!.GetValue<int>());
    }

    [Fact]
    public async Task NestedKeyPath_IsResolved()
    {
        const string json = """{ "items": [ { "m": { "id": "u1" } }, { "m": { "id": "u2" } }, { "m": { "id": "u1" } } ] }""";

        var (ctx, _, _) = await RunAsync(json, Config("$.m.id"));

        Assert.Equal(2, ctx.Get<JsonArray>("$.groups")!.Count);
    }

    [Fact]
    public async Task SourceArray_IsLeftUntouched()
    {
        var (ctx, _, _) = await RunAsync("""{ "items": [ { "k": "a" }, { "k": "a" } ] }""", Config());

        Assert.Equal(2, ctx.Get<JsonArray>("$.items")!.Count);
    }

    [Fact]
    public async Task OneThousandItemsInOneGroup()
    {
        var items = string.Join(",", Enumerable.Range(0, 1000).Select(i => $$"""{ "k": "same", "n": {{i}} }"""));

        var (ctx, _, _) = await RunAsync($$"""{ "items": [ {{items}} ] }""", Config());

        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Single(groups);
        var grouped = groups[0]!["items"]!.AsArray();
        Assert.Equal(1000, grouped.Count);
        Assert.Equal(Enumerable.Range(0, 1000), grouped.Select(i => i!["n"]!.GetValue<int>()));
    }

    [Fact]
    public async Task OneThousandGroups_IsLinear()
    {
        var items = string.Join(",", Enumerable.Range(0, 1000).Select(i => $$"""{ "k": "key-{{i}}" }"""));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var (ctx, _, _) = await RunAsync($$"""{ "items": [ {{items}} ] }""", Config());

        watch.Stop();
        var groups = ctx.Get<JsonArray>("$.groups")!;
        Assert.Equal(1000, groups.Count);
        Assert.Equal("key-0", groups[0]!["key"]!.GetValue<string>());
        Assert.Equal("key-999", groups[999]!["key"]!.GetValue<string>());
        Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Result_CanBeIteratedByForEach()
    {
        const string json = """{ "items": [ { "k": "a" }, { "k": "b" }, { "k": "a" } ] }""";
        var (ctx, nodeContext) = Prepare(json, Config());
        await new GroupByNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(ctx, nodeContext);

        var counter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(counter);
        A.CallTo(() => counter.GetNext()).Returns(0);
        var forEachConfig = new ForEachNodeConfiguration
        {
            Path = "$.groups",
            IterationPath = "$.groups",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration> { new TestNodeConfiguration { TargetPath = "$.key" } }
        };
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), _logger, ctx);
        var forEachContext = root.RegisterChildNode("ForEach", 0, forEachConfig, ctx);

        await new ForEachNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(ctx, forEachContext);

        A.CallTo(() => counter.GetNext()).MustHaveHappened(2, Times.Exactly);
        Assert.Equal(2, ctx.Length("$.Result"));
    }
}
