using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class ConcatNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private (IDataContext, INodeContext) Prepare(string json, ConcatNodeConfiguration config)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Concat", 0, config, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_WithStaticValues_ConcatenatesStrings()
    {
        var (dataContext, nodeContext) = Prepare("{\"name\":\"test\"}", new ConcatNodeConfiguration
        {
            Path = "$",
            ConcatSubPath = "result",
            Parts = new List<ConcatItem>
            {
                new() { Value = "Hello" },
                new() { Value = " " },
                new() { Value = "World" }
            }
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConcatNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello World", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithValuePaths_ConcatenatesFromPaths()
    {
        var (dataContext, nodeContext) = Prepare("{\"firstName\":\"John\",\"lastName\":\"Doe\"}", new ConcatNodeConfiguration
        {
            Path = "$",
            ConcatSubPath = "fullName",
            Parts = new List<ConcatItem>
            {
                new() { ValuePath = "$.firstName" },
                new() { Value = " " },
                new() { ValuePath = "$.lastName" }
            }
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConcatNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("John Doe", dataContext.Get<string>("$.fullName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithMixedValuesAndPaths_ConcatenatesCorrectly()
    {
        var (dataContext, nodeContext) = Prepare("{\"name\":\"Alice\"}", new ConcatNodeConfiguration
        {
            Path = "$",
            ConcatSubPath = "greeting",
            Parts = new List<ConcatItem>
            {
                new() { Value = "Hello, " },
                new() { ValuePath = "$.name" },
                new() { Value = "!" }
            }
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConcatNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello, Alice!", dataContext.Get<string>("$.greeting"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithEmptyParts_ReturnsEmptyString()
    {
        var (dataContext, nodeContext) = Prepare("{\"name\":\"test\"}", new ConcatNodeConfiguration
        {
            Path = "$",
            ConcatSubPath = "result",
            Parts = new List<ConcatItem>()
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConcatNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ConcatNode_BooleanValue_RendersCapitalized()
    {
        // Pre-fix: JsonNode.ToJsonString() rendered "true" (lowercase) for the
        // boolean-valued ValuePath. Post-fix: JsonStringifyHelper.ToLegacyString
        // preserves legacy Newtonsoft parity ("True"/"False"), aligning with
        // FormatStringNode and HashNode.
        var (dataContext, nodeContext) = Prepare(
            "{\"enabled\":true,\"disabled\":false}",
            new ConcatNodeConfiguration
            {
                Path = "$",
                ConcatSubPath = "result",
                Parts = new List<ConcatItem>
                {
                    new() { ValuePath = "$.enabled" },
                    new() { Value = "/" },
                    new() { ValuePath = "$.disabled" }
                }
            });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConcatNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<string>("$.result");
        Assert.Equal("True/False", result);
        Assert.DoesNotContain("true", result);
        Assert.DoesNotContain("false", result);
    }

    [Fact]
    public async Task ProcessObjectAsync_WithArrayPath_ConcatenatesForEachElement()
    {
        var (dataContext, nodeContext) = Prepare(
            "{\"items\":[{\"prefix\":\"A\",\"suffix\":\"1\"},{\"prefix\":\"B\",\"suffix\":\"2\"}]}",
            new ConcatNodeConfiguration
            {
                Path = "$.items[*]",
                ConcatSubPath = "combined",
                Parts = new List<ConcatItem>
                {
                    new() { ValuePath = "$.prefix" },
                    new() { Value = "-" },
                    new() { ValuePath = "$.suffix" }
                }
            });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConcatNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("A-1", dataContext.Get<string>("$.items[0].combined"));
        Assert.Equal("B-2", dataContext.Get<string>("$.items[1].combined"));
    }
}
