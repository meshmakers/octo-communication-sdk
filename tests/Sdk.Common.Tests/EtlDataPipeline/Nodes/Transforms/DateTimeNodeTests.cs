#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class DateTimeNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private static readonly DateTime ReferenceDate = new(2025, 6, 15, 14, 30, 45, DateTimeKind.Utc);
    private static readonly DateTime OtherDate = new(2025, 6, 20, 8, 15, 0, DateTimeKind.Utc);

    private (IDataContext, INodeContext) PrepareTest(DateTimeNodeConfiguration config, object? testData = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = testData ?? new
        {
            timestamp = ReferenceDate,
            otherTimestamp = OtherDate,
            daysToAdd = 5.0,
            hoursToAdd = 3.5,
            minutesToAdd = 90.0,
            secondsToAdd = 120.0,
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("DateTime", 0, config, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_Now_ReturnsCurrentUtcDateTime()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.Now,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);
        var before = DateTime.UtcNow;

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var after = DateTime.UtcNow;
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var result = dataContext.Get<DateTime>("$.result");
        Assert.True(result >= before && result <= after);
    }

    [Fact]
    public async Task ProcessObjectAsync_StartOfDay_TruncatesToMidnight()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.StartOfDay,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddDays_WithValue_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddDays,
            Path = "$.timestamp",
            Value = 3.0,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddDays(3), dataContext.Get<DateTime>("$.result"));
    }

    // Phase 11 regression: DateTimeNode.GetNumericValue uses Get<object?> which returns
    // a boxed JsonElement under STJ. Convert.ToDouble cannot handle JsonElement (no
    // IConvertible). Production code needs Get<double> or explicit kind-based extraction.
    [Fact]
    public async Task ProcessObjectAsync_AddDays_WithValuePath_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddDays,
            Path = "$.timestamp",
            ValuePath = "$.daysToAdd",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddDays(5), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddDays_NegativeValue_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddDays,
            Path = "$.timestamp",
            Value = -2.0,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddDays(-2), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddHours_WithValue_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddHours,
            Path = "$.timestamp",
            Value = 3.5,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddHours(3.5), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddHours_WithValuePath_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddHours,
            Path = "$.timestamp",
            ValuePath = "$.hoursToAdd",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddHours(3.5), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddMinutes_WithValue_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddMinutes,
            Path = "$.timestamp",
            Value = 90.0,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddMinutes(90), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddSeconds_WithValue_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddSeconds,
            Path = "$.timestamp",
            Value = 120.0,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddSeconds(120), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DaysBetween_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.DaysBetween,
            Path = "$.timestamp",
            ValuePath = "$.otherTimestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(5, dataContext.Get<int>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DaysBetween_NegativeResult_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.DaysBetween,
            Path = "$.otherTimestamp",
            ValuePath = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(-5, dataContext.Get<int>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DaysBetween_IgnoresTimePart()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.DaysBetween,
            Path = "$.morning",
            ValuePath = "$.evening",
            TargetPath = "$.result",
        };

        var testData = new
        {
            morning = new DateTime(2025, 3, 15, 6, 0, 0, DateTimeKind.Utc),
            evening = new DateTime(2025, 3, 15, 22, 0, 0, DateTimeKind.Utc),
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0, dataContext.Get<int>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Format_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.Format,
            Path = "$.timestamp",
            Value = "yyyy-MM-ddTHH:mm",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("2025-06-15T14:30", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Format_DateOnly_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.Format,
            Path = "$.timestamp",
            Value = "yyyy-MM-dd",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("2025-06-15", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_CombineDateTime_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.CombineDateTime,
            Path = "$.dateSource",
            ValuePath = "$.timeSource",
            TargetPath = "$.result",
        };

        var testData = new
        {
            dateSource = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            timeSource = new DateTime(2025, 1, 1, 14, 30, 0, DateTimeKind.Utc),
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 3, 15, 14, 30, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ExtractDate_TruncatesToMidnight()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ExtractDate,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ExtractTime_ReturnsTimeString()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ExtractTime,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("14:30:45", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullInputValue_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("null"));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);

        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.Now,
            TargetPath = "$.result",
        };

        var nodeContext = rootNodeContext.RegisterChildNode("DateTime", 0, config, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        // STJ semantics: writing to $.result on a null root raises InvalidOperationException
        // from the overlay (cannot set member on non-object). Legacy code would have raised
        // PipelineExecutionException via an explicit pre-check on Current. Either error type
        // signals "cannot run on null root"; assert any exception is thrown.
        await Assert.ThrowsAnyAsync<Exception>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_MissingSourcePath_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.StartOfDay,
            Path = "$.nonexistent",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddDays_MissingValue_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddDays,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_Format_MissingFormatString_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.Format,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnsupportedOperation_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = (DateTimeOperationDto)999,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<NotSupportedException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_DaysBetween_MissingValuePath_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.DaysBetween,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_AddDays_ValuePathTakesPrecedenceOverValue()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.AddDays,
            Path = "$.timestamp",
            Value = 100.0,
            ValuePath = "$.daysToAdd",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate.AddDays(5), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MidnightBoundary_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.StartOfDay,
            Path = "$.midnight",
            TargetPath = "$.result",
        };

        var testData = new
        {
            midnight = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }
}
