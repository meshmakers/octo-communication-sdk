using System.Globalization;
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
            Path = "$.v",
            TargetPath = "$.asDouble",
            ValueType = AttributeValueTypesDto.Double
        }, dataContext);
        await testee.ProcessObjectAsync(dataContext, toDouble);
        Assert.Equal(5.0, dataContext.Get<double>("$.asDouble"));

        var backToInt = rootNodeContext.RegisterChildNode("ToInt", 1, new ConvertDataTypeNodeConfiguration
        {
            Path = "$.asDouble",
            TargetPath = "$.asInt",
            ValueType = AttributeValueTypesDto.Int
        }, dataContext);
        await testee.ProcessObjectAsync(dataContext, backToInt);
        Assert.Equal(5, dataContext.Get<int>("$.asInt"));
    }

    // ---- AB#7509: devices (e.g. MQTT smart meters) send every value as a JSON string ----------
    // "234.20" must become 234.2 with the decimal POINT whatever the host culture is (de-AT uses a
    // decimal comma and a thousands point).

    [Theory]
    [InlineData("de-AT", "234.20", 234.2)]
    [InlineData("de-AT", "12345.678", 12345.678)]
    [InlineData("de-AT", "-0.5", -0.5)]
    [InlineData("de-AT", "512", 512.0)]
    [InlineData("fr-FR", "234.20", 234.2)]
    [InlineData("en-US", "234.20", 234.2)]
    public async Task ConvertDataType_NumericString_ToDouble_IsCultureIndependent(string culture, string text,
        double expected)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var ctx = await ConvertAsync($"{{\"v\":\"{text}\"}}", "$.v", AttributeValueTypesDto.Double);
            Assert.Equal(expected, ctx.Get<double>("$.Demo"));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Fact]
    public async Task ConvertDataType_NumericString_ToInt_IsCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            var ctx = await ConvertAsync("{\"v\":\"512\"}", "$.v", AttributeValueTypesDto.Int);
            Assert.Equal(512, ctx.Get<int>("$.Demo"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("n/a")]
    [InlineData("")]
    [InlineData("234,20")]
    public async Task ConvertDataType_NonNumericString_ToDouble_ThrowsNamingPath(string text)
    {
        var e = await Assert.ThrowsAsync<DataPipelineException>(
            () => ConvertAsync($"{{\"v\":\"{text}\"}}", "$.v", AttributeValueTypesDto.Double));

        Assert.Contains("$.v", e.Message);
    }

    [Fact]
    public async Task ConvertDataType_QuotedDottedKey_AfterAnEarlierWrite_ConvertsTheValue()
    {
        // A smart-meter payload: OBIS codes are keys with dots. The second conversion runs after the first
        // one has written to the context; it used to read nothing and convert it to 0 without an error.
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"v\":{\"32.7.0\":\"234.20\",\"1.8.0\":\"12345.678\"}}"));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var testee = new ConvertDataTypeNode(A.Fake<NodeDelegate>());

        foreach (var (index, key, target) in new[] { (0u, "32.7.0", "voltage"), (1u, "1.8.0", "energy") })
        {
            var nodeContext = rootNodeContext.RegisterChildNode("Convert", index, new ConvertDataTypeNodeConfiguration
            {
                Path = $"$.v['{key}']",
                TargetPath = $"$.reading.{target}",
                ValueType = AttributeValueTypesDto.Double
            }, dataContext);
            await testee.ProcessObjectAsync(dataContext, nodeContext);
        }

        Assert.Equal(234.2, dataContext.Get<double>("$.reading.voltage"));
        Assert.Equal(12345.678, dataContext.Get<double>("$.reading.energy"));
    }

    // AB#7540: onMissing. Missing key = "$.v.x" absent below an existing parent; missing parent = "$.p.q" with no "p".
    private const string MissingDoc = "{\"v\":{\"a\":1}}";

    private async Task<(DataContextImpl Data, IPipelineLogger Logger, NodeDelegate Next, Exception? Error)> RunOnMissingAsync(
        string path, AttributeValueTypesDto type, ConvertMissingBehaviorDto? onMissing)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(MissingDoc));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var cfg = new ConvertDataTypeNodeConfiguration { Path = path, TargetPath = "$.out", ValueType = type };
        if (onMissing.HasValue) cfg.OnMissing = onMissing.Value;
        var nodeContext = root.RegisterChildNode("Convert", 0, cfg, dataContext);
        var next = A.Fake<NodeDelegate>();
        Exception? error = null;
        try { await new ConvertDataTypeNode(next).ProcessObjectAsync(dataContext, nodeContext); }
        catch (Exception e) { error = e; }
        return (dataContext, logger, next, error);
    }

    [Theory]
    [InlineData("$.v.x")]
    [InlineData("$.p.q")]
    public async Task OnMissing_Default_Double_WritesZeroAndWarnsOnce(string path)
    {
        // Unset and explicit Default behave the same and keep today's output.
        foreach (ConvertMissingBehaviorDto? mode in new ConvertMissingBehaviorDto?[] { null, ConvertMissingBehaviorDto.Default })
        {
            var r = await RunOnMissingAsync(path, AttributeValueTypesDto.Double, mode);
            Assert.Null(r.Error);
            Assert.Equal(0d, r.Data.Get<double>("$.out"));
            Assert.True(r.Data.Exists("$.out"));
            A.CallTo(() => r.Logger.Warning(A<string>._, A<string>._, A<string>.That.Contains("does not exist"), A<object[]>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => r.Next.Invoke(A<IDataContext>._, A<INodeContext>._)).MustHaveHappenedOnceExactly();
        }
    }

    [Theory]
    [InlineData("$.v.x")]
    [InlineData("$.p.q")]
    public async Task OnMissing_Default_String_WritesNullAndWarnsOnce(string path)
    {
        var r = await RunOnMissingAsync(path, AttributeValueTypesDto.String, null);
        Assert.Null(r.Error);
        Assert.Null(r.Data.Get<string>("$.out"));
        A.CallTo(() => r.Logger.Warning(A<string>._, A<string>._, A<string>.That.Contains("does not exist"), A<object[]>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("$.v.x", AttributeValueTypesDto.Double)]
    [InlineData("$.p.q", AttributeValueTypesDto.Double)]
    [InlineData("$.v.x", AttributeValueTypesDto.String)]
    [InlineData("$.p.q", AttributeValueTypesDto.String)]
    public async Task OnMissing_Skip_DoesNotWriteTargetAndContinues(string path, AttributeValueTypesDto type)
    {
        var r = await RunOnMissingAsync(path, type, ConvertMissingBehaviorDto.Skip);
        Assert.Null(r.Error);
        Assert.False(r.Data.Exists("$.out"));
        A.CallTo(() => r.Next.Invoke(A<IDataContext>._, A<INodeContext>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => r.Logger.Warning(A<string>._, A<string>._, A<string>._, A<object[]>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("$.v.x", AttributeValueTypesDto.Double)]
    [InlineData("$.p.q", AttributeValueTypesDto.Double)]
    [InlineData("$.v.x", AttributeValueTypesDto.String)]
    [InlineData("$.p.q", AttributeValueTypesDto.String)]
    public async Task OnMissing_Fail_ThrowsNamingThePath(string path, AttributeValueTypesDto type)
    {
        var r = await RunOnMissingAsync(path, type, ConvertMissingBehaviorDto.Fail);
        Assert.IsType<DataPipelineException>(r.Error);
        Assert.Contains(path, r.Error!.Message);
        Assert.False(r.Data.Exists("$.out"));
        A.CallTo(() => r.Next.Invoke(A<IDataContext>._, A<INodeContext>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(ConvertMissingBehaviorDto.Default)]
    [InlineData(ConvertMissingBehaviorDto.Skip)]
    [InlineData(ConvertMissingBehaviorDto.Fail)]
    public async Task OnMissing_ExistingValue_IsConvertedInEveryMode(ConvertMissingBehaviorDto mode)
    {
        var r = await RunOnMissingAsync("$.v.a", AttributeValueTypesDto.Double, mode);
        Assert.Null(r.Error);
        Assert.Equal(1d, r.Data.Get<double>("$.out"));
        A.CallTo(() => r.Logger.Warning(A<string>._, A<string>._, A<string>._, A<object[]>._)).MustNotHaveHappened();
    }

    [Fact]
    public void OnMissing_DefaultsToDefault()
    {
        Assert.Equal(ConvertMissingBehaviorDto.Default,
            new ConvertDataTypeNodeConfiguration { Path = "$.a", TargetPath = "$.b", ValueType = AttributeValueTypesDto.Int }.OnMissing);
    }
}
