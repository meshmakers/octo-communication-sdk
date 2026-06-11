using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class MapNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(MapNodeConfiguration mapNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(Generator.GenerateColumnDataNode().ToJsonString()));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Map", 0, mapNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_NestedDottedSelectPath_ResolvesValue()
    {
        // Phase 2.5.2 walker collapse: SelectPaths now resolve through JsonPathWalker
        // instead of the bespoke dotted-only walker. The full dialect (brackets, wildcards,
        // filters, recursive descent) is now available on the *read* side. Brackets are NOT
        // exercised here because each SelectPath is reused as the output entry key and
        // JsonNodePath.Set is dotted-only by design — see the JsonNodePath class
        // contract. So the meaningful regression check that survives the symmetric path
        // constraint is: a deeply nested dotted path keeps working through the new helper.
        const string json = """
        {
            "data": {
                "metrics": {
                    "first": [10, 20, 30],
                    "second": [40, 50, 60]
                }
            }
        }
        """;
        MapNodeConfiguration mapNodeConfiguration = new()
        {
            Path = "$.data",
            TargetPath = "$.result",
            SelectPaths = new List<string>
            {
                "$.metrics.first",
                "$.metrics.second"
            }
        };

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Map", 0, mapNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new MapNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.result");
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0]!["metrics"]!["first"]!.GetValue<int>());
        Assert.Equal(40, result[0]!["metrics"]!["second"]!.GetValue<int>());
        Assert.Equal(60, result[2]!["metrics"]!["second"]!.GetValue<int>());
    }

    [Fact]
    public async Task ProcessObjectAsync_Simple_OK()
    {
        MapNodeConfiguration mapNodeConfiguration = new()
        {
            Path = "$.data",
            TargetPath = "$.result",
            SelectPaths = new List<string>
            {
                "$.timestamp", "$.batteryPower", "$.productionPower", "$.additionalProductionPower",
                "$.batteryStateOfCharge", "$.consumption", "$.net"
            }
        };

        var (dataContext, nodeContext) = PrepareTest(mapNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new MapNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.result");
        Assert.NotNull(result);
        Assert.Equal(6, result.Count);
    }
}
