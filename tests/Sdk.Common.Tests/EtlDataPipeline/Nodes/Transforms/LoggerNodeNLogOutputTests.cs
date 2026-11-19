using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Layouts;
using NLog.Targets;
using Sdk.Common.Tests.Fixtures;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Pins what a Logger@1 record looks like on the wire (AB#9335): the adapters log through NLog with
/// a <see cref="JsonLayout" /> that includes the event properties (octo-mesh-adapter
/// <c>src/MeshAdapter/nlog.config</c>), and Dash0 promotes the JSON fields to attributes.
/// </summary>
public sealed class LoggerNodeNLogOutputTests : IClassFixture<ServiceCollectionFixture>, IDisposable
{
    private const string NodePath = "PipelineExecution/Logger";

    private readonly ServiceCollectionFixture _fixture;
    private readonly MemoryTarget _target;
    private readonly LogFactory _logFactory = new();
    private readonly ILoggerFactory _loggerFactory;

    public LoggerNodeNLogOutputTests(ServiceCollectionFixture fixture)
    {
        _fixture = fixture;
        _target = new MemoryTarget("memory")
        {
            Layout = new JsonLayout
            {
                IncludeEventProperties = true,
                Attributes =
                {
                    new JsonAttribute("level", "${level:uppercase=true}"),
                    new JsonAttribute("message", "${message}")
                }
            }
        };
        var configuration = new LoggingConfiguration(_logFactory);
        configuration.AddRuleForAllLevels(_target);
        _logFactory.Configuration = configuration;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new NLogLoggerProvider(new NLogProviderOptions(), _logFactory)));
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logFactory.Dispose();
    }

    private DefaultPipelineLogger CreatePipelineLogger()
    {
        return new DefaultPipelineLogger(_loggerFactory.CreateLogger<DefaultPipelineLogger>());
    }

    private JsonElement SingleLine()
    {
        var line = Assert.Single(_target.Logs);
        return JsonDocument.Parse(line).RootElement;
    }

    private async Task RunLoggerNodeAsync(LoggerNodeConfiguration configuration, string json = "{}")
    {
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.TenantId).Returns("logtenant");
        A.CallTo(() => etlContext.PipelineRtEntityId)
            .Returns(new RtEntityId("System.Communication/Pipeline", new OctoObjectId("6650a1b2c3d4e5f600000001")));
        var services = new ServiceCollection();
        foreach (var descriptor in _fixture.Services) services.Add(descriptor);
        services.AddSingleton(etlContext);

        using var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var root = NodeContext.CreateRootNodeContext(services.BuildServiceProvider(), CreatePipelineLogger(),
            dataContext);
        var nodeContext = root.RegisterChildNode("Logger", 0, configuration, dataContext);
        await new LoggerNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dataContext, nodeContext);
    }

    [Fact]
    public void Baseline_InfoWithoutArguments_WritesNodePathPrefixAndMessage()
    {
        // The pre-AB#9335 path Logger@1 used (nodeContext.Info(message)); other nodes still use it.
        CreatePipelineLogger().Info("0:Logger", NodePath, "Test log message");

        var line = SingleLine();
        Assert.Equal("INFO", line.GetProperty("level").GetString());
        Assert.Equal($"{NodePath}: Test log message", line.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("Value {0} and {name} and { unbalanced } } {")]
    [InlineData("{{escaped}} {0:N2} {-1}")]
    public void Baseline_InfoWithoutArguments_KeptBracesVerbatimInNLog(string text)
    {
        // Pinned before the change: without arguments the template path did not mangle braces in
        // NLog either. Logger@1 now passes the message as a value, so that no longer depends on it.
        CreatePipelineLogger().Info("0:Logger", NodePath, text);

        Assert.Equal($"{NodePath}: {text}", SingleLine().GetProperty("message").GetString());
    }

    [Fact]
    public async Task StaticMessage_KeepsTodaysBodyAndLevel()
    {
        await RunLoggerNodeAsync(new LoggerNodeConfiguration { Message = "Test log message" });

        var line = SingleLine();
        Assert.Equal("INFO", line.GetProperty("level").GetString());
        Assert.Equal($"{NodePath}: Test log message", line.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MessagePath_BodyIsNodePathPrefixAndResolvedMessage_WithAttributesAsProperties()
    {
        await RunLoggerNodeAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            Level = LoggerNodeLevel.Warning,
            Attributes =
            [
                new LoggerNodeAttribute { Name = "MeteringPoint", ValuePath = "$.mp" },
                new LoggerNodeAttribute { Name = "Reason", Value = "UnknownMeteringPoint" }
            ]
        }, """{"text":"EDA_UNKNOWN_MP AT001","mp":"AT001"}""");

        var line = SingleLine();
        Assert.Equal("WARN", line.GetProperty("level").GetString());
        Assert.Equal($"{NodePath}: EDA_UNKNOWN_MP AT001", line.GetProperty("message").GetString());
        Assert.Equal("logtenant", line.GetProperty("TenantId").GetString());
        Assert.Equal("6650a1b2c3d4e5f600000001", line.GetProperty("PipelineRtId").GetString());
        Assert.Equal(NodePath, line.GetProperty("NodePath").GetString());
        Assert.Equal("AT001", line.GetProperty("MeteringPoint").GetString());
        Assert.Equal("UnknownMeteringPoint", line.GetProperty("Reason").GetString());
    }

    [Theory]
    [InlineData("Value {0} and {name} and { unbalanced } } {")]
    [InlineData("{{escaped}} {0:N2} {-1}")]
    public async Task Braces_ReachTheLogLineVerbatim(string text)
    {
        await RunLoggerNodeAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            Attributes = [new LoggerNodeAttribute { Name = "Text", ValuePath = "$.text" }]
        }, JsonSerializer.Serialize(new { text }));

        var line = SingleLine();
        Assert.Equal($"{NodePath}: {text}", line.GetProperty("message").GetString());
        Assert.Equal(text, line.GetProperty("Text").GetString());
    }

    [Theory]
    [InlineData(LogLevel.Trace, "DEBUG")]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Information, "INFO")]
    [InlineData(LogLevel.Warning, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "ERROR")]
    public void StructuredLog_MapsTheLevel(LogLevel level, string expected)
    {
        CreatePipelineLogger().Log(level, "0:Logger", NodePath, "m", []);

        Assert.Equal(expected, SingleLine().GetProperty("level").GetString());
    }
}
