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

    // ---- AB#5275: a JSON number whose raw text has a fraction -------------------------------
    // Integral doubles are serialized with a trailing ".0" by design (Newtonsoft parity), so any
    // value written by Math@1 / LinearScaler@1 / SumAggregation@1 / ExecuteCSharp@1 arrives here
    // looking like "5.0". STJ's built-in Int32 converter rejected that on its raw text.

    /// <summary>Runs one ConvertDataType node over <paramref name="json" /> and returns the context.</summary>
    private async Task<DataContextImpl> ConvertAsync(string json, string path,
        AttributeValueTypesDto valueType, string targetPath = "$.Demo")
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ConvertData", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = path,
            TargetPath = targetPath,
            ValueType = valueType
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        return dataContext;
    }

    [Fact]
    public async Task ConvertDataType_IntegralDouble_ToInt_ReturnsInt()
    {
        // The reported pipeline, 1:1 — int1 always worked, int2 threw.
        const string json = "{\"int1\":4,\"int2\":5.0}";

        var fromInt1 = await ConvertAsync(json, "$.int1", AttributeValueTypesDto.Int);
        var fromInt2 = await ConvertAsync(json, "$.int2", AttributeValueTypesDto.Int);

        Assert.Equal(4, fromInt1.Get<int>("$.Demo"));
        Assert.Equal(5, fromInt2.Get<int>("$.Demo"));
    }

    [Theory]
    [InlineData("5.7", 6)]
    [InlineData("5.5", 6)]
    [InlineData("4.5", 4)]  // banker's rounding — NOT 5
    [InlineData("-5.5", -6)]
    public async Task ConvertDataType_FractionalDouble_ToInt_RoundsToEven(string literal, int expected)
    {
        var ctx = await ConvertAsync($"{{\"v\":{literal}}}", "$.v", AttributeValueTypesDto.Int);
        Assert.Equal(expected, ctx.Get<int>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_IntegralDouble_ToInt64_ReturnsLong()
    {
        var ctx = await ConvertAsync("{\"v\":1e10}", "$.v", AttributeValueTypesDto.Int64);
        Assert.Equal(10000000000L, ctx.Get<long>("$.Demo"));
    }

    [Fact]
    public async Task ConvertDataType_OutOfRange_ToInt_ThrowsNamingPathAndType()
    {
        var e = await Assert.ThrowsAsync<DataPipelineException>(
            () => ConvertAsync("{\"v\":1e10}", "$.v", AttributeValueTypesDto.Int));

        Assert.Contains("$.v", e.Message);
        Assert.Contains("Int", e.Message);
        Assert.Contains("range of System.Int32", e.Message);
    }

    [Fact]
    public async Task ConvertDataType_ChainedAfterDouble_RoundTrips()
    {
        // The real pipeline shape: a node writes a double, a later node reads it as Int. The
        // intermediate value is a CLR-backed JsonValue, not a parsed element.
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"v\":5}"));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new ConvertDataTypeNode(fn);

        var toDouble = rootNodeContext.RegisterChildNode("ToDouble", 0, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.v", TargetPath = "$.asDouble", ValueType = AttributeValueTypesDto.Double
        }, dataContext);
        await testee.ProcessObjectAsync(dataContext, toDouble);
        Assert.Equal(5.0, dataContext.Get<double>("$.asDouble"));

        var backToInt = rootNodeContext.RegisterChildNode("ToInt", 1, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.asDouble", TargetPath = "$.asInt", ValueType = AttributeValueTypesDto.Int
        }, dataContext);
        await testee.ProcessObjectAsync(dataContext, backToInt);
        Assert.Equal(5, dataContext.Get<int>("$.asInt"));
    }
}
