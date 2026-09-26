using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.Services;

/// <summary>
/// AB#5385: the status line a trigger reports after every poll goes through
/// <see cref="AdapterPipelineExecutionReporter.ReportPipelineStatusAsync" />. Pins the two
/// halves of its contract — the DTO the hub client receives, and that a delivery failure neither
/// escapes nor floods the log (one Debug line per <see cref="AdapterPipelineExecutionReporter.StatusFailureLogInterval" />).
/// </summary>
public class AdapterPipelineExecutionReporterTests
{
    private readonly IAdapterHubClient _hubClient = A.Fake<IAdapterHubClient>();
    private readonly CapturingLogger _logger = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 9, 26, 17, 40, 0, TimeSpan.Zero));
    private readonly RtEntityId _pipelineRtEntityId =
        new("System.Communication/Pipeline", OctoObjectId.GenerateNewId());

    private AdapterPipelineExecutionReporter CreateSut() => new(_hubClient, _logger, _clock);

    [Fact]
    public async Task ReportPipelineStatusAsync_SendsTheLineAsDto()
    {
        var timestamp = new DateTime(2026, 9, 26, 17, 40, 12, DateTimeKind.Utc);
        PipelineStatusReportDto? sent = null;
        A.CallTo(() => _hubClient.ReportPipelineStatusAsync(A<PipelineStatusReportDto>._))
            .Invokes((PipelineStatusReportDto dto) => sent = dto)
            .Returns(Task.CompletedTask);

        await CreateSut().ReportPipelineStatusAsync(_pipelineRtEntityId, "seen 12, imported 12", isError: false,
            timestamp);

        Assert.NotNull(sent);
        Assert.Equal(_pipelineRtEntityId, sent!.PipelineRtEntityId);
        Assert.Equal("seen 12, imported 12", sent.Message);
        Assert.False(sent.IsError);
        Assert.Equal(timestamp, sent.TimestampUtc);
    }

    [Fact]
    public async Task ReportPipelineStatusAsync_ErrorFlagIsForwarded()
    {
        PipelineStatusReportDto? sent = null;
        A.CallTo(() => _hubClient.ReportPipelineStatusAsync(A<PipelineStatusReportDto>._))
            .Invokes((PipelineStatusReportDto dto) => sent = dto)
            .Returns(Task.CompletedTask);

        await CreateSut().ReportPipelineStatusAsync(_pipelineRtEntityId, "ERROR folder not found", isError: true,
            DateTime.UtcNow);

        Assert.True(sent!.IsError);
    }

    [Fact]
    public async Task ReportPipelineStatusAsync_HubFailure_DoesNotThrowAndLogsAtDebug()
    {
        // The connection-not-active case of a fire-and-forget SendAsync (hub down, reconnecting).
        A.CallTo(() => _hubClient.ReportPipelineStatusAsync(A<PipelineStatusReportDto>._))
            .ThrowsAsync(new InvalidOperationException("The connection is not active"));

        await CreateSut().ReportPipelineStatusAsync(_pipelineRtEntityId, "line", false, DateTime.UtcNow);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("AB#5385", entry.Message);
    }

    [Fact]
    public async Task ReportPipelineStatusAsync_RepeatedFailures_LogOncePerInterval()
    {
        // A poll loop reports every interval; against an old or unreachable controller every
        // report fails. One Debug line per window, the rest counted and folded into the next one.
        A.CallTo(() => _hubClient.ReportPipelineStatusAsync(A<PipelineStatusReportDto>._))
            .ThrowsAsync(new InvalidOperationException("The connection is not active"));
        var sut = CreateSut();

        for (var i = 0; i < 5; i++)
        {
            await sut.ReportPipelineStatusAsync(_pipelineRtEntityId, $"line {i}", false, DateTime.UtcNow);
            _clock.Advance(TimeSpan.FromSeconds(30));
        }

        Assert.Single(_logger.Entries);

        // Past the window the next failure is logged again and names how many were swallowed.
        _clock.Advance(AdapterPipelineExecutionReporter.StatusFailureLogInterval);
        await sut.ReportPipelineStatusAsync(_pipelineRtEntityId, "line 6", false, DateTime.UtcNow);

        Assert.Equal(2, _logger.Entries.Count);
        Assert.All(_logger.Entries, e => Assert.Equal(LogLevel.Debug, e.Level));
        Assert.Contains("4 earlier failure(s)", _logger.Entries[1].Message);
    }

    [Fact]
    public async Task ReportPipelineStatusAsync_Success_LogsNothingAboveTrace()
    {
        await CreateSut().ReportPipelineStatusAsync(_pipelineRtEntityId, "line", false, DateTime.UtcNow);

        Assert.All(_logger.Entries, e => Assert.Equal(LogLevel.Trace, e.Level));
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class CapturingLogger : ILogger<AdapterPipelineExecutionReporter>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
