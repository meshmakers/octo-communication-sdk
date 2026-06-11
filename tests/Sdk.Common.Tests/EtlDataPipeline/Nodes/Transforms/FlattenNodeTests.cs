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

public class FlattenNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(FlattenNodeConfiguration flattenNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var json = JsonSerializer.Serialize(Generator.GenerateOrder(), SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Flatten", 0, flattenNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    private (IDataContext, INodeContext) PrepareTestWithN(FlattenNodeConfiguration flattenNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var json = JsonSerializer.Serialize(Generator.GenerateOrders(), SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Flatten", 0, flattenNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_Simple_OK()
    {
        var configuration = new FlattenNodeConfiguration
        {
            Path = "$.Items[*].OrderItemId",
            TargetPath = "$.Result",
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FlattenNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.Result");
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ProcessObjectAsync_Complex_OK()
    {
        var configuration = new FlattenNodeConfiguration
        {
            Path = "$.Items[*].Product",
            TargetPath = "$.Result",
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FlattenNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.Result");
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ProcessObjectAsync_Array_ByJsonPath_OK()
    {
        var configuration = new FlattenNodeConfiguration
        {
            Path = "$.Orders[*].Items[*]",
            TargetPath = "$.Result",
        };

        var (dataContext, nodeContext) = PrepareTestWithN(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FlattenNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.Result");
        Assert.NotNull(result);
        Assert.Equal(30, result.Count);
    }

    [Fact]
    public async Task ProcessObjectAsync_WithoutJsonPath_OK()
    {
        var configuration = new FlattenNodeConfiguration
        {
            Path = "$.Orders[*].Items",
            TargetPath = "$.Result",
        };

        var (dataContext, nodeContext) = PrepareTestWithN(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FlattenNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<JsonArray>("$.Result");
        Assert.NotNull(result);
        Assert.Equal(10, result.Count);
    }
}
