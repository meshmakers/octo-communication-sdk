using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class DistinctNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private (IDataContext, INodeContext) Prepare(string json, DistinctNodeConfiguration config)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Distinct", 0, config, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task DistinctNode_KeyPathWithBracket_DistinguishesByDeepValue()
    {
        // Phase 2.5.2 capability gain: DistinctValuePath now resolves through JsonPathWalker
        // instead of the bespoke dotted-only walker. Pre-fix the bracket index "[0]" was treated
        // literally by Split('.') so resolution silently failed and every item was filtered out.
        // Post-fix the index is honoured: dedup is by metadata.tags[0].
        const string json = """
        {
            "items": [
                { "metadata": { "tags": ["alpha", "extra"] } },
                { "metadata": { "tags": ["beta", "extra"] } },
                { "metadata": { "tags": ["alpha", "different"] } }
            ]
        }
        """;
        var config = new DistinctNodeConfiguration
        {
            Path = "$.items",
            TargetPath = "$.distinct",
            DistinctValuePath = "$.metadata.tags[0]"
        };

        var (dataContext, nodeContext) = Prepare(json, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DistinctNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.distinct");
        Assert.NotNull(result);
        // Two distinct first-tags: "alpha" and "beta".
        Assert.Equal(2, result.Count);
        Assert.Equal("alpha", result[0]!["metadata"]!["tags"]![0]!.GetValue<string>());
        Assert.Equal("beta", result[1]!["metadata"]!["tags"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task DistinctNode_ScalarArray_DeduplicatesValues()
    {
        // Baseline coverage: scalar dedup path still works after the walker collapse.
        const string json = """{ "values": [1, 2, 2, 3, 3, 3, 4] }""";
        var config = new DistinctNodeConfiguration
        {
            Path = "$.values",
            TargetPath = "$.distinct"
        };

        var (dataContext, nodeContext) = Prepare(json, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DistinctNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var result = dataContext.Get<JsonArray>("$.distinct");
        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public async Task DistinctNode_StringValuesThatLookLikeDates_StayDistinct()
    {
        // Pre-fix: ConvertNodeToValue eagerly called JsonElement.TryGetDateTime for any
        // string-kind value, collapsing "2024-01-01" and "2024-01-01T00:00:00" to the same
        // DateTime instance and merging them in distinctness comparison (only 1 distinct value).
        // Post-fix: strings stay strings — both are preserved as separate keys (2 distinct values).
        // Pre-migration JTokenType.Date was only set when the parser had already classified the
        // value as a date; plain strings stayed strings and were per-string distinct.
        const string json = """{ "values": ["2024-01-01", "2024-01-01T00:00:00"] }""";
        var config = new DistinctNodeConfiguration
        {
            Path = "$.values",
            TargetPath = "$.distinct"
        };

        var (dataContext, nodeContext) = Prepare(json, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DistinctNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var result = dataContext.Get<JsonArray>("$.distinct");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("2024-01-01", result[0]!.GetValue<string>());
        Assert.Equal("2024-01-01T00:00:00", result[1]!.GetValue<string>());
    }
}
