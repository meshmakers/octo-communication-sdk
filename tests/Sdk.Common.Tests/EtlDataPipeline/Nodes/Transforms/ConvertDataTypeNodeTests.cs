using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class ConvertDataTypeNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    [Fact]
    public async Task ProcessObjectAsync_WithPath_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"Value\":6}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.Value",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.String
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("6", dataContext.Get<string>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_StringTrue_ToBoolean_ReturnsTrue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"Value\":\"true\"}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.Value",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.Boolean
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.True(dataContext.Get<bool>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_StringFalse_ToBoolean_ReturnsFalse()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"Value\":\"false\"}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.Value",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.Boolean
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.False(dataContext.Get<bool>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_StringIso8601_ToDateTime_Parses()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"Value\":\"2026-05-08T12:00:00Z\"}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.Value",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.DateTime
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var actual = dataContext.Get<DateTime>("$.Demo");
        var expected = new DateTime(2026, 5, 8, 12, 0, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.Equal(expected.ToUniversalTime(), actual.ToUniversalTime());
    }

    [Fact]
    public async Task ConvertDataType_StringInt_ToInt_ParsesValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"v\":\"42\"}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.v",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.Int
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(42, dataContext.Get<int>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_StringInt64_ToInt64_ParsesValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"v\":\"9999999999\"}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.v",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.Int64
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(9999999999L, dataContext.Get<long>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_StringDouble_ToDouble_ParsesValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"v\":\"3.14\"}"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.v",
            TargetPath = "$.Demo",
            ValueType = AttributeValueTypesDto.Double
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.14, dataContext.Get<double>("$.Demo"));
    }
}
