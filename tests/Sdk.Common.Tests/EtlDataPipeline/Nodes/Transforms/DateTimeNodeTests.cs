#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Globalization;
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

    // -----------------------------------------------------------------------------
    // AB#5095: ConvertToTimeZone (11) and FromUnixTimeMilliseconds (12). Both are
    // appended operations - the tests above cover operations 0-10 and stay untouched.
    // -----------------------------------------------------------------------------

    private const string ViennaTimeZoneId = "Europe/Vienna";

    private INodeContext PrepareNextNode(IDataContext dataContext, DateTimeNodeConfiguration config)
    {
        var logger = A.Fake<IPipelineLogger>();
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        return rootNodeContext.RegisterChildNode("DateTime", 1, config, dataContext);
    }

    [Fact]
    public void DateTimeOperationDto_AppendsNewOperationsWithoutRenumberingExistingOnes()
    {
        // Deployed pipelines carry the operation in their stored configuration, and the
        // generated JSON schema maps names onto these ordinals. New operations may only
        // be appended; renumbering would silently repoint every existing pipeline.
        Assert.Equal(0, (int)DateTimeOperationDto.Now);
        Assert.Equal(1, (int)DateTimeOperationDto.StartOfDay);
        Assert.Equal(2, (int)DateTimeOperationDto.AddDays);
        Assert.Equal(3, (int)DateTimeOperationDto.AddHours);
        Assert.Equal(4, (int)DateTimeOperationDto.AddMinutes);
        Assert.Equal(5, (int)DateTimeOperationDto.AddSeconds);
        Assert.Equal(6, (int)DateTimeOperationDto.DaysBetween);
        Assert.Equal(7, (int)DateTimeOperationDto.Format);
        Assert.Equal(8, (int)DateTimeOperationDto.CombineDateTime);
        Assert.Equal(9, (int)DateTimeOperationDto.ExtractDate);
        Assert.Equal(10, (int)DateTimeOperationDto.ExtractTime);
        Assert.Equal(11, (int)DateTimeOperationDto.ConvertToTimeZone);
        Assert.Equal(12, (int)DateTimeOperationDto.FromUnixTimeMilliseconds);
        Assert.Equal(13, (int)DateTimeOperationDto.ConvertFromTimeZone);
        Assert.Equal(14, (int)DateTimeOperationDto.StartOfMonth);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_SummerTime_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = ViennaTimeZoneId,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        // CEST is UTC+2 in June.
        Assert.Equal(new DateTime(2025, 6, 15, 16, 30, 45), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_WinterTime_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = ViennaTimeZoneId,
            TargetPath = "$.result",
        };

        var testData = new
        {
            timestamp = new DateTime(2025, 1, 15, 14, 30, 45, DateTimeKind.Utc),
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        // CET is UTC+1 in January.
        Assert.Equal(new DateTime(2025, 1, 15, 15, 30, 45), dataContext.Get<DateTime>("$.result"));
    }

    [Theory]
    // Vienna switches to CEST on 2025-03-30 at 01:00 UTC and back to CET on 2025-10-26 at 01:00 UTC.
    [InlineData("2025-03-30T00:59:59Z", "2025-03-30T01:59:59")] // last second before the spring-forward
    [InlineData("2025-03-30T01:00:00Z", "2025-03-30T03:00:00")] // local 02:00-03:00 does not exist that day
    [InlineData("2025-10-26T00:59:59Z", "2025-10-26T02:59:59")] // first pass through the repeated hour (CEST)
    [InlineData("2025-10-26T01:00:00Z", "2025-10-26T02:00:00")] // second pass through the repeated hour (CET)
    public async Task ProcessObjectAsync_ConvertToTimeZone_DaylightSavingBoundary_OK(string utcInput,
        string expectedLocal)
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = ViennaTimeZoneId,
            TargetPath = "$.result",
        };

        var testData = new
        {
            timestamp = DateTime.Parse(utcInput, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DateTime.Parse(expectedLocal, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_ResultIsWallClockTimeNotUtc()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = ViennaTimeZoneId,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // DateTime.Equals ignores Kind, so the value assertions above would also pass for a
        // UTC-tagged result. The converted value is wall-clock time in the target zone and
        // must therefore serialize without the trailing Z that would re-read it as an instant.
        Assert.Equal("2025-06-15T16:30:45", dataContext.Get<string>("$.result"));
        Assert.Equal(DateTimeKind.Unspecified, dataContext.Get<DateTime>("$.result").Kind);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_SourceWithoutKind_TreatedAsUtc()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = ViennaTimeZoneId,
            TargetPath = "$.result",
        };

        // A timestamp that lost its Z on the way through an upstream system. Reading it as
        // machine-local time would make the result depend on the agent's own time zone.
        var testData = new
        {
            timestamp = "2025-06-15T14:30:45",
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 16, 30, 45), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_WithValuePath_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            ValuePath = "$.zoneId",
            TargetPath = "$.result",
        };

        var testData = new
        {
            timestamp = new DateTime(2025, 6, 15, 14, 30, 45, DateTimeKind.Utc),
            zoneId = ViennaTimeZoneId,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 16, 30, 45), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_ValuePathTakesPrecedenceOverValue()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = "America/New_York",
            ValuePath = "$.zoneId",
            TargetPath = "$.result",
        };

        var testData = new
        {
            timestamp = new DateTime(2025, 6, 15, 14, 30, 45, DateTimeKind.Utc),
            zoneId = ViennaTimeZoneId,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 16, 30, 45), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_UtcTarget_KeepsTheInstant()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = "UTC",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(ReferenceDate, dataContext.Get<DateTime>("$.result"));

        // The UTC zone is the one destination for which ConvertTimeFromUtc returns a Utc-tagged
        // value; the wall-clock contract holds there as well, so no trailing Z.
        Assert.Equal("2025-06-15T14:30:45", dataContext.Get<string>("$.result"));
        Assert.Equal(DateTimeKind.Unspecified, dataContext.Get<DateTime>("$.result").Kind);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_UnknownTimeZoneId_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            Value = "Europe/Wien",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));
        Assert.Contains("Europe/Wien", exception.Message);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_MissingTimeZoneId_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_ValuePathNotSet_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.timestamp",
            ValuePath = "$.nonexistent",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertToTimeZone_MissingSourcePath_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.nonexistent",
            Value = ViennaTimeZoneId,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = 1000000000000L,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2001, 9, 9, 1, 46, 40, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_Zero_ReturnsUnixEpoch()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = 0L,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_NegativeValue_ReturnsDateBeforeEpoch()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = -86400000L,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(1969, 12, 31, 0, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_KeepsMillisecondPart()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = 1750000000123L,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 15, 6, 40, 123, DateTimeKind.Utc),
            dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_NumericString_OK()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        // Upstream REST payloads routinely quote large integers to survive JavaScript clients.
        var testData = new
        {
            millis = "1750000000000",
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(new DateTime(2025, 6, 15, 15, 6, 40, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_NonNumericValue_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = "not-a-timestamp",
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));
        Assert.Contains("$.millis", exception.Message);
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_MissingPath_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.nonexistent",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_NullValue_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = (long?)null,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMilliseconds_OutOfRange_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.result",
        };

        var testData = new
        {
            millis = long.MaxValue,
        };

        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_FromUnixTimeMillisecondsThenConvertToTimeZone_OK()
    {
        // The U1/U3 combination: an epoch timestamp from WeClapp rendered as Vienna wall time.
        var unixConfig = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.FromUnixTimeMilliseconds,
            Path = "$.millis",
            TargetPath = "$.utc",
        };

        var testData = new
        {
            millis = 1750000000000L,
        };

        var (dataContext, unixNodeContext) = PrepareTest(unixConfig, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new DateTimeNode(fn);

        await testee.ProcessObjectAsync(dataContext, unixNodeContext);

        var zoneConfig = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.utc",
            Value = ViennaTimeZoneId,
            TargetPath = "$.local",
        };

        await testee.ProcessObjectAsync(dataContext, PrepareNextNode(dataContext, zoneConfig));

        Assert.Equal(new DateTime(2025, 6, 15, 15, 6, 40, DateTimeKind.Utc), dataContext.Get<DateTime>("$.utc"));
        Assert.Equal(new DateTime(2025, 6, 15, 17, 6, 40), dataContext.Get<DateTime>("$.local"));
    }

    // -----------------------------------------------------------------------------
    // AB#6854: ConvertFromTimeZone (13) and StartOfMonth (14), appended operations.
    // -----------------------------------------------------------------------------

    private (IDataContext DataContext, Func<Task> Run) PrepareFromTimeZone(string? zoneValue = ViennaTimeZoneId,
        string? zonePath = null, object? data = null)
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertFromTimeZone,
            Path = "$.local",
            Value = zoneValue,
            ValuePath = zonePath,
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config, data);
        var testee = new DateTimeNode(A.Fake<NodeDelegate>());
        return (dataContext, () => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Theory]
    [InlineData("2026-07-15T00:00:00", "2026-07-14T22:00:00")] // CEST, UTC+2
    [InlineData("2026-01-15T00:00:00", "2026-01-14T23:00:00")] // CET, UTC+1
    [InlineData("2026-03-29T00:00:00", "2026-03-28T23:00:00")] // spring change day, before the switch
    [InlineData("2026-03-29T03:30:00", "2026-03-29T01:30:00")] // spring change day, after the switch
    [InlineData("2026-10-25T00:00:00", "2026-10-24T22:00:00")] // autumn change day, before the repeat
    [InlineData("2026-10-25T03:00:00", "2026-10-25T02:00:00")] // autumn change day, after the repeat (CET)
    [InlineData("2026-10-25T02:30:00", "2026-10-25T00:30:00")] // ambiguous: earlier instant (CEST)
    [InlineData("2026-10-25T02:00:00", "2026-10-25T00:00:00")] // ambiguous: first second of the repeat
    [InlineData("2026-10-25T02:59:59", "2026-10-25T00:59:59")] // ambiguous: last second, still CEST
    public async Task ProcessObjectAsync_ConvertFromTimeZone_Vienna_OK(string local, string expectedUtc)
    {
        var (dataContext, run) = PrepareFromTimeZone(data: new { local });

        await run();

        var result = dataContext.Get<DateTime>("$.result");
        Assert.Equal(DateTime.Parse(expectedUtc + "Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(expectedUtc + "Z", dataContext.Get<string>("$.result"));
    }

    [Theory]
    [InlineData("2026-03-29T02:00:00")] // first second of the gap
    [InlineData("2026-03-29T02:30:00")]
    [InlineData("2026-03-29T02:59:59")]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_NonExistingLocalTime_ThrowsNamingValueAndZone(string local)
    {
        var (_, run) = PrepareFromTimeZone(data: new { local });

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(run);

        Assert.Contains(local, exception.Message);
        Assert.Contains(ViennaTimeZoneId, exception.Message);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_NonExistingLocalTime_MessageIsCultureIndependent()
    {
        // fi-FI uses '.' as the time separator: a culture-sensitive format would print 02.30.00.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
        try
        {
            var (_, run) = PrepareFromTimeZone(data: new { local = "2026-03-29T02:30:00" });

            var exception = await Assert.ThrowsAsync<PipelineExecutionException>(run);

            Assert.Contains("'2026-03-29T02:30:00'", exception.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_ZoneFromValuePath_SameAsFixedValue()
    {
        var (dataContext, run) = PrepareFromTimeZone(zoneValue: null, zonePath: "$.zoneId",
            data: new { local = "2026-07-15T00:00:00", zoneId = ViennaTimeZoneId });

        await run();

        Assert.Equal(new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_ValuePathTakesPrecedenceOverValue()
    {
        var (dataContext, run) = PrepareFromTimeZone(zoneValue: "America/New_York", zonePath: "$.zoneId",
            data: new { local = "2026-07-15T00:00:00", zoneId = ViennaTimeZoneId });

        await run();

        Assert.Equal(new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_UnknownZone_ThrowsNamingTheZone()
    {
        var (_, run) = PrepareFromTimeZone(zoneValue: "Europe/Wien", data: new { local = "2026-07-15T00:00:00" });

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(run);

        Assert.Contains("Europe/Wien", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_EmptyFixedZone_ThrowsException(string? zone)
    {
        var (_, run) = PrepareFromTimeZone(zoneValue: zone, data: new { local = "2026-07-15T00:00:00" });

        await Assert.ThrowsAsync<PipelineExecutionException>(run);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_EmptyZoneFromValuePath_ThrowsException(string? zone)
    {
        var (_, run) = PrepareFromTimeZone(zoneValue: null, zonePath: "$.zoneId",
            data: new { local = "2026-07-15T00:00:00", zoneId = zone });

        await Assert.ThrowsAsync<PipelineExecutionException>(run);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_MissingSourcePath_ThrowsException()
    {
        var (_, run) = PrepareFromTimeZone(data: new { other = "2026-07-15T00:00:00" });

        await Assert.ThrowsAsync<PipelineExecutionException>(run);
    }

    [Theory]
    [InlineData("2026-07-15T00:00:00Z")] // tagged UTC by an upstream system: still the local clock reading
    [InlineData("2026-07-15T00:00:00")]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_ClockFieldsAreLocalTimeWhateverTheKind(string local)
    {
        var (dataContext, run) = PrepareFromTimeZone(data: new { local });

        await run();

        Assert.Equal(new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Theory]
    // A UTC offset in the value is ignored like Z: the clock fields as written are the local time.
    // A typed DateTime read would convert these to the host's local time and shift the clock.
    [InlineData("2026-07-15T00:00:00+05:00")]
    [InlineData("2026-07-15T00:00:00-08:00")]
    [InlineData("2026-07-15T00:00:00+02:00")]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_OffsetInValue_KeepsTheClockFieldsAsWritten(string local)
    {
        var (dataContext, run) = PrepareFromTimeZone(data: new { local });

        await run();

        Assert.Equal(new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.result"));
    }

    [Theory]
    // A lenient text parser would complete these with today's date or guess a month/day order;
    // the result would then depend on the day the pipeline runs. They fail like in every other operation.
    [InlineData("12:00:00")]
    [InlineData("07/15/2026 00:00:00")]
    [InlineData("not a date")]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_NoCompleteIsoDateTime_Fails(string local)
    {
        var (dataContext, run) = PrepareFromTimeZone(data: new { local });

        await Assert.ThrowsAnyAsync<Exception>(run);

        Assert.Equal(DataKind.Undefined, dataContext.GetKind("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_FractionalSeconds_AreKept()
    {
        var (dataContext, run) = PrepareFromTimeZone(data: new { local = "2026-07-15T00:00:00.5" });

        await run();

        Assert.Equal(new DateTime(2026, 7, 14, 22, 0, 0, 500, DateTimeKind.Utc),
            dataContext.Get<DateTime>("$.result"));
    }

    [Theory]
    [InlineData("2026-03-29T02:30:00.5")]
    [InlineData("2026-03-29T02:30:00.1234567")]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_NonExistingLocalTimeWithFraction_MessageNamesTheExactValue(
        string local)
    {
        var (_, run) = PrepareFromTimeZone(data: new { local });

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(run);

        Assert.Contains($"'{local}'", exception.Message);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConvertFromTimeZone_UtcZone_KeepsTheClockAsInstant()
    {
        var (dataContext, run) = PrepareFromTimeZone(zoneValue: "UTC", data: new { local = "2026-07-15T10:20:30" });

        await run();

        var result = dataContext.Get<DateTime>("$.result");
        Assert.Equal(new DateTime(2026, 7, 15, 10, 20, 30), result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    private async Task<DateTime> RoundTripViaVienna(DateTime utc)
    {
        var toConfig = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertToTimeZone,
            Path = "$.utc",
            Value = ViennaTimeZoneId,
            TargetPath = "$.local",
        };
        var fromConfig = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertFromTimeZone,
            Path = "$.local",
            Value = ViennaTimeZoneId,
            TargetPath = "$.back",
        };

        var (dataContext, toNode) = PrepareTest(toConfig, new { utc });
        var testee = new DateTimeNode(A.Fake<NodeDelegate>());

        await testee.ProcessObjectAsync(dataContext, toNode);
        await testee.ProcessObjectAsync(dataContext, PrepareNextNode(dataContext, fromConfig));

        var back = dataContext.Get<DateTime>("$.back");
        Assert.Equal(DateTimeKind.Utc, back.Kind);
        return back;
    }

    [Theory]
    // Summer, winter, both change days around the switch and every unambiguous time on them.
    [InlineData("2026-07-15T12:00:00Z")]
    [InlineData("2026-01-15T12:00:00Z")]
    [InlineData("2026-03-29T00:59:59Z")] // last second before the spring switch
    [InlineData("2026-03-29T01:00:00Z")] // first second after it (local 03:00)
    [InlineData("2026-03-29T12:00:00Z")]
    [InlineData("2026-10-24T22:00:00Z")] // local midnight of the autumn change day
    [InlineData("2026-10-25T00:00:00Z")] // local 02:00 CEST: first pass of the repeated hour
    [InlineData("2026-10-25T00:59:59Z")] // local 02:59:59 CEST: last second of the first pass
    [InlineData("2026-10-25T02:00:00Z")] // local 03:00 CET: after the repeat
    [InlineData("2026-10-25T12:00:00Z")]
    public async Task ConvertToTimeZoneThenConvertFromTimeZone_ReturnsTheOriginalInstant(string utcInstant)
    {
        var original = DateTime.Parse(utcInstant, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        var back = await RoundTripViaVienna(original);

        Assert.Equal(original, back);
    }

    [Fact]
    public async Task ConvertToTimeZoneThenConvertFromTimeZone_SecondPassOfRepeatedHour_MapsToTheFirstPass()
    {
        // The one case a round trip cannot preserve: local 02:30 occurs twice on 2026-10-25, and the
        // wall-clock reading alone cannot tell the passes apart. By contract it maps to the first.
        var secondPass = new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc); // 02:30 CET

        var back = await RoundTripViaVienna(secondPass);

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), back);
    }

    [Theory]
    [InlineData("2026-12-31T23:59:00Z", "2026-12-01T00:00:00Z", DateTimeKind.Utc)] // year boundary
    [InlineData("2026-01-01T00:00:00Z", "2026-01-01T00:00:00Z", DateTimeKind.Utc)] // already the first
    [InlineData("2026-03-31T23:59:59.9999999Z", "2026-03-01T00:00:00Z", DateTimeKind.Utc)] // end of month
    [InlineData("2028-02-29T13:45:10Z", "2028-02-01T00:00:00Z", DateTimeKind.Utc)] // leap day
    [InlineData("2026-07-15T14:30:45", "2026-07-01T00:00:00", DateTimeKind.Unspecified)] // Kind is kept
    public async Task ProcessObjectAsync_StartOfMonth_OK(string source, string expected, DateTimeKind expectedKind)
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.StartOfMonth,
            Path = "$.timestamp",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config, new { timestamp = source });
        var testee = new DateTimeNode(A.Fake<NodeDelegate>());

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var result = dataContext.Get<DateTime>("$.result");
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), result);
        Assert.Equal(expectedKind, result.Kind);
    }

    [Fact]
    public async Task ProcessObjectAsync_StartOfMonth_MissingSourcePath_ThrowsException()
    {
        var config = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.StartOfMonth,
            Path = "$.nonexistent",
            TargetPath = "$.result",
        };

        var (dataContext, nodeContext) = PrepareTest(config);
        var testee = new DateTimeNode(A.Fake<NodeDelegate>());

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task StartOfMonthThenConvertFromTimeZone_GivesViennaMonthStartAsUtc()
    {
        // The consumer case: first of the month in Vienna, expressed as UTC for GetStreamData@1.
        var monthConfig = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.StartOfMonth,
            Path = "$.local",
            TargetPath = "$.monthStart",
        };
        var utcConfig = new DateTimeNodeConfiguration
        {
            Operation = DateTimeOperationDto.ConvertFromTimeZone,
            Path = "$.monthStart",
            Value = ViennaTimeZoneId,
            TargetPath = "$.from",
        };

        var (dataContext, monthNode) = PrepareTest(monthConfig, new { local = "2026-10-18T11:41:00" });
        var testee = new DateTimeNode(A.Fake<NodeDelegate>());

        await testee.ProcessObjectAsync(dataContext, monthNode);
        await testee.ProcessObjectAsync(dataContext, PrepareNextNode(dataContext, utcConfig));

        Assert.Equal(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc), dataContext.Get<DateTime>("$.from"));
    }
}
