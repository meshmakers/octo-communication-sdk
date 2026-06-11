#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Extracts;

public class SetPrimitiveValueNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(SetPrimitiveValueNodeConfiguration configuration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            existingValue = "original",
            existingNumber = 42,
            nested = new
            {
                property = "value"
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetPrimitiveValue", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_SetString_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newStringValue",
            Value = "test string",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("test string", dataContext.Get<string>("$.newStringValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetInt_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newIntValue",
            Value = 123,
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(123, dataContext.Get<int>("$.newIntValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetIntFromString_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.convertedIntValue",
            Value = "456",
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(456, dataContext.Get<int>("$.convertedIntValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetInt64_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newLongValue",
            Value = 9876543210L,
            ValueType = AttributeValueTypesDto.Int64,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(9876543210L, dataContext.Get<long>("$.newLongValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetDouble_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newDoubleValue",
            Value = 3.14159,
            ValueType = AttributeValueTypesDto.Double,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.14159, dataContext.Get<double>("$.newDoubleValue"), 5);
    }

    [Fact]
    public async Task ProcessObjectAsync_SetDoubleFromString_InvariantCulture_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.parsedDoubleValue",
            Value = "123.456789",
            ValueType = AttributeValueTypesDto.Double,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(123.456789, dataContext.Get<double>("$.parsedDoubleValue"), 6);
    }

    [Fact]
    public async Task ProcessObjectAsync_SetBoolean_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newBooleanValue",
            Value = true,
            ValueType = AttributeValueTypesDto.Boolean,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.True(dataContext.Get<bool>("$.newBooleanValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetBooleanFromString_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.convertedBooleanValue",
            Value = "false",
            ValueType = AttributeValueTypesDto.Boolean,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.False(dataContext.Get<bool>("$.convertedBooleanValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetDateTime_OK()
    {
        var expectedDate = new DateTime(2023, 10, 15, 14, 30, 0, DateTimeKind.Utc);
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newDateTimeValue",
            Value = expectedDate,
            ValueType = AttributeValueTypesDto.DateTime,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(expectedDate, dataContext.Get<DateTime>("$.newDateTimeValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetDateTimeFromString_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.parsedDateTimeValue",
            Value = "2023-12-25T00:00:00Z",
            ValueType = AttributeValueTypesDto.DateTime,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<DateTime>("$.parsedDateTimeValue");
        Assert.Equal(new DateTime(2023, 12, 25, 0, 0, 0), result.Date);
    }

    [Fact]
    public async Task ProcessObjectAsync_SetTimeSpan_OK()
    {
        var expectedTimeSpan = TimeSpan.FromHours(2.5);
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newTimeSpanValue",
            Value = expectedTimeSpan,
            ValueType = AttributeValueTypesDto.TimeSpan,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(expectedTimeSpan, dataContext.Get<TimeSpan>("$.newTimeSpanValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetBinary_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newBinaryValue",
            Value = (byte)255,
            ValueType = AttributeValueTypesDto.Binary,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal((byte)255, dataContext.Get<byte>("$.newBinaryValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetStringArray_OK()
    {
        var expectedArray = new[] { "item1", "item2", "item3" };
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newStringArrayValue",
            Value = expectedArray,
            ValueType = AttributeValueTypesDto.StringArray,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<string[]>("$.newStringArrayValue");
        Assert.Equal(expectedArray, result);
    }

    [Fact]
    public async Task ProcessObjectAsync_SetIntArray_OK()
    {
        var expectedArray = new[] { 1, 2, 3, 4, 5 };
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.newIntArrayValue",
            Value = expectedArray,
            ValueType = AttributeValueTypesDto.IntArray,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<int[]>("$.newIntArrayValue");
        Assert.Equal(expectedArray, result);
    }

    [Fact]
    public async Task ProcessObjectAsync_SetNestedPath_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nested.newProperty",
            Value = "nested value",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("nested value", dataContext.Get<string>("$.nested.newProperty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_OverwriteExistingValue_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.existingValue",
            Value = "overwritten value",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("overwritten value", dataContext.Get<string>("$.existingValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValue_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullValue",
            Value = null!,
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Null(dataContext.Get<string>("$.nullValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnsupportedValueType_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.testValue",
            Value = "test",
            ValueType = (AttributeValueTypesDto)999,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_InvalidConversion_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.testValue",
            Value = "not a number",
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_InvalidDateTimeFormat_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.testValue",
            Value = "invalid date format",
            ValueType = AttributeValueTypesDto.DateTime,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_ZeroValues_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.zeroValue",
            Value = 0,
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0, dataContext.Get<int>("$.zeroValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyString_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.emptyStringValue",
            Value = "",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.emptyStringValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyArray_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.emptyArrayValue",
            Value = new string[0],
            ValueType = AttributeValueTypesDto.StringArray,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<string[]>("$.emptyArrayValue");
        Assert.Empty(result!);
    }

    [Fact]
    public async Task ProcessObjectAsync_LargeNumbers_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.largeNumberValue",
            Value = double.MaxValue,
            ValueType = AttributeValueTypesDto.Double,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(double.MaxValue, dataContext.Get<double>("$.largeNumberValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NegativeNumbers_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.negativeValue",
            Value = -42,
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(-42, dataContext.Get<int>("$.negativeValue"));
    }

    // Phase 11 regression: SetPrimitiveValueNode resolves ValuePath via Get<object?>
    // which returns boxed JsonElement under STJ; ConvertToConfiguredType then calls
    // Convert.ToInt32/etc on the JsonElement and fails (no IConvertible). The legacy
    // Newtonsoft path returned JValue (which implements IConvertible). Same root cause
    // as DateTimeNode/ExecuteCSharpNode regressions.
    [Fact]
    public async Task ProcessObjectAsync_SetStringFromValuePath_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.copiedValue",
            ValuePath = "$.existingValue",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("original", dataContext.Get<string>("$.copiedValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetIntFromValuePath_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.copiedNumber",
            ValuePath = "$.existingNumber",
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(42, dataContext.Get<int>("$.copiedNumber"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SetFromNestedValuePath_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.extractedProperty",
            ValuePath = "$.nested.property",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("value", dataContext.Get<string>("$.extractedProperty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ValuePathTakesPrecedenceOverValue_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.resultValue",
            Value = "static value",
            ValuePath = "$.existingValue",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("original", dataContext.Get<string>("$.resultValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ValuePathNotFound_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.resultValue",
            ValuePath = "$.nonexistentPath",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyValuePath_UsesStaticValue_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.resultValue",
            Value = "static value",
            ValuePath = "",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("static value", dataContext.Get<string>("$.resultValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValuePath_UsesStaticValue_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.resultValue",
            Value = "static value",
            ValuePath = null,
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("static value", dataContext.Get<string>("$.resultValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ValuePathWithTypeConversion_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.doubleFromInt",
            ValuePath = "$.existingNumber",
            ValueType = AttributeValueTypesDto.Double,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(42.0, dataContext.Get<double>("$.doubleFromInt"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueForString_OK()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullStringValue",
            Value = null,
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Null(dataContext.Get<string>("$.nullStringValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueForInt_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullIntValue",
            Value = null,
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueForDouble_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullDoubleValue",
            Value = null,
            ValueType = AttributeValueTypesDto.Double,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueForBoolean_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullBooleanValue",
            Value = null,
            ValueType = AttributeValueTypesDto.Boolean,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueForDateTime_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullDateTimeValue",
            Value = null,
            ValueType = AttributeValueTypesDto.DateTime,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueForStringArray_ThrowsException()
    {
        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.nullArrayValue",
            Value = null,
            ValueType = AttributeValueTypesDto.StringArray,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueFromValuePath_ForString_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            nullProperty = (string?)null,
            existingValue = "original"
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.copiedNullValue",
            ValuePath = "$.nullProperty",
            ValueType = AttributeValueTypesDto.String,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetPrimitiveValue", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Null(dataContext.Get<string>("$.copiedNullValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValueFromValuePath_ForInt_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            nullProperty = (int?)null,
            existingValue = "original"
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var configuration = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.copiedNullValue",
            ValuePath = "$.nullProperty",
            ValueType = AttributeValueTypesDto.Int,
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetPrimitiveValue", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SetPrimitiveValueNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }
}
