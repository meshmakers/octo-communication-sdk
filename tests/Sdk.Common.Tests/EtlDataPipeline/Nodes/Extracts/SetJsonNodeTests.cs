using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Extracts;

public class SetJsonNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private static IDataContext CreateContext() => new DataContextImpl(JsonDocument.Parse("{}"));

    [Fact]
    public async Task ProcessObjectAsync_WithSimpleJsonObject_SetsValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetJson", 0, new SetJsonNodeConfiguration
        {
            TargetPath = "$.data",
            JsonString = "{\"name\":\"test\",\"value\":42}"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetJsonNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Object, dataContext.GetKind("$.data"));
        Assert.Equal("test", dataContext.Get<string>("$.data.name"));
        Assert.Equal(42, dataContext.Get<int>("$.data.value"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithNestedJsonObject_SetsValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetJson", 0, new SetJsonNodeConfiguration
        {
            TargetPath = "$.config",
            JsonString = "{\"settings\":{\"enabled\":true,\"timeout\":30}}"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetJsonNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Object, dataContext.GetKind("$.config.settings"));
        Assert.True(dataContext.Get<bool>("$.config.settings.enabled"));
        Assert.Equal(30, dataContext.Get<int>("$.config.settings.timeout"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithEmptyJsonObject_SetsEmptyObject()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetJson", 0, new SetJsonNodeConfiguration
        {
            TargetPath = "$.empty",
            JsonString = "{}"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetJsonNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Object, dataContext.GetKind("$.empty"));
        Assert.Empty(dataContext.Keys("$.empty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithJsonArray_SetsArray()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetJson", 0, new SetJsonNodeConfiguration
        {
            TargetPath = "$.data",
            JsonString = "[1,2,3]"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetJsonNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.data"));
        Assert.Equal(3, dataContext.Length("$.data"));
        Assert.Equal(1, dataContext.Get<int>("$.data[0]"));
        Assert.Equal(2, dataContext.Get<int>("$.data[1]"));
        Assert.Equal(3, dataContext.Get<int>("$.data[2]"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithInvalidJson_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetJson", 0, new SetJsonNodeConfiguration
        {
            TargetPath = "$.data",
            JsonString = "invalid json"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetJsonNode(fn);

        // STJ throws JsonException; legacy threw Newtonsoft.Json.JsonReaderException.
        await Assert.ThrowsAnyAsync<Exception>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_OverwritesExistingValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"data\":{\"old\":\"value\"}}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetJson", 0, new SetJsonNodeConfiguration
        {
            TargetPath = "$.data",
            JsonString = "{\"new\":\"value\"}"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetJsonNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("value", dataContext.Get<string>("$.data.new"));
        Assert.True(dataContext.Exists("$.data.new"));
        Assert.False(dataContext.Exists("$.data.old"));
    }
}
