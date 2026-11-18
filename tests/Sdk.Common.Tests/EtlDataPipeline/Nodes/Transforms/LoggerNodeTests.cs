using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Logger@1 (AB#9335): static or DataContext message, level, structured attributes, missing values,
/// limits, redaction, verbatim braces and configuration validation. The emitted body and the NLog
/// properties are pinned in <see cref="LoggerNodeNLogOutputTests" />.
/// </summary>
public class LoggerNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private const string TenantId = "logtenant";
    private const string NodePath = "PipelineExecution/Logger";
    private static readonly OctoObjectId PipelineRtId = OctoObjectId.GenerateNewId();

    // Obviously fake test value, never a real credential.
    private const string Secret = "fake-revealed-password-42";

    private async Task<(RecordingPipelineLogger Logger, NodeDelegate Next, INodeContext NodeContext)> RunAsync(
        LoggerNodeConfiguration configuration, string json = "{}", Action<INodeContext>? beforeRun = null)
    {
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.TenantId).Returns(TenantId);
        A.CallTo(() => etlContext.PipelineRtEntityId)
            .Returns(new RtEntityId("System.Communication/Pipeline", PipelineRtId));
        var services = new ServiceCollection();
        foreach (var descriptor in fixture.Services) services.Add(descriptor);
        services.AddSingleton(etlContext);

        var logger = new RecordingPipelineLogger();
        using var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var root = NodeContext.CreateRootNodeContext(services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = root.RegisterChildNode("Logger", 0, configuration, dataContext);
        beforeRun?.Invoke(nodeContext);

        var next = A.Fake<NodeDelegate>();
        await new LoggerNode(next).ProcessObjectAsync(dataContext, nodeContext);
        return (logger, next, nodeContext);
    }

    private static string? Attribute(RecordingPipelineLogger.Entry entry, string name)
    {
        return entry.Attributes.Single(a => a.Key == name).Value as string;
    }

    [Fact]
    public async Task StaticMessageOnly_LogsUnchangedAtInformation_AndCallsNext()
    {
        var (logger, next, nodeContext) = await RunAsync(new LoggerNodeConfiguration { Message = "Test log message" });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Test log message", entry.Message);
        Assert.Equal(NodePath, entry.NodePath);
        A.CallTo(() => next.Invoke(A<IDataContext>._, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task EmptyMessage_IsLogged_AndCallsNext()
    {
        var (logger, next, _) = await RunAsync(new LoggerNodeConfiguration { Message = "" });

        Assert.Equal("", Assert.Single(logger.Entries).Message);
        A.CallTo(() => next.Invoke(A<IDataContext>._, A<INodeContext>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Record_CarriesTenantPipelineAndNodePathAttributes()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration { Message = "m" });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(TenantId, Attribute(entry, "TenantId"));
        Assert.Equal(PipelineRtId.ToString(), Attribute(entry, "PipelineRtId"));
        Assert.Equal(NodePath, Attribute(entry, "NodePath"));
    }

    [Fact]
    public async Task WithoutAnEtlContext_LogsWithNodePathOnly()
    {
        // A host that registered only a derived ETL context cannot resolve IEtlContext; the line is
        // still written, without the tenant and pipeline attributes.
        var logger = new RecordingPipelineLogger();
        using var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = root.RegisterChildNode("Logger", 0, new LoggerNodeConfiguration { Message = "m" }, dataContext);

        await new LoggerNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(["NodePath"], Assert.Single(logger.Entries).Attributes.Select(a => a.Key));
    }

    [Fact]
    public async Task MessagePath_ReadsTheMessageFromTheDataContext()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration { MessagePath = "$.text" },
            """{"text":"EDA message skipped for AT001"}""");

        Assert.Equal("EDA message skipped for AT001", Assert.Single(logger.Entries).Message);
    }

    [Theory]
    [InlineData("""{}""", "$.text")]
    [InlineData("""{"text":null}""", "$.text")]
    [InlineData("""{"a":{"b":1}}""", "$.a.x.y")]
    public async Task MissingOrNullMessage_LogsMissingMarker_AndCallsNext(string json, string path)
    {
        var (logger, next, _) = await RunAsync(new LoggerNodeConfiguration { MessagePath = path }, json);

        Assert.Equal(LoggerNodeConfiguration.DefaultMissingValue, Assert.Single(logger.Entries).Message);
        A.CallTo(() => next.Invoke(A<IDataContext>._, A<INodeContext>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task MissingValue_ReplacesTheDefaultMarker()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            MissingValue = "n/a",
            Attributes = [new LoggerNodeAttribute { Name = "Id", ValuePath = "$.id" }]
        });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("n/a", entry.Message);
        Assert.Equal("n/a", Attribute(entry, "Id"));
    }

    [Theory]
    [InlineData("""{"v":{"b":1,"c":[1,2]}}""", """{"b":1,"c":[1,2]}""")]
    [InlineData("""{"v":[1,"x",true]}""", """[1,"x",true]""")]
    [InlineData("""{"v":42.5}""", "42.5")]
    [InlineData("""{"v":true}""", "true")]
    [InlineData("""{"v":"text"}""", "text")]
    public async Task NonStringValues_RenderAsCompactJson(string json, string expected)
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.v",
            Attributes = [new LoggerNodeAttribute { Name = "Value", ValuePath = "$.v" }]
        }, json);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expected, entry.Message);
        Assert.Equal(expected, Attribute(entry, "Value"));
    }

    [Theory]
    [InlineData(LoggerNodeLevel.Debug, LogLevel.Debug)]
    [InlineData(LoggerNodeLevel.Information, LogLevel.Information)]
    [InlineData(LoggerNodeLevel.Warning, LogLevel.Warning)]
    [InlineData(LoggerNodeLevel.Error, LogLevel.Error)]
    public async Task Level_IsEmitted(LoggerNodeLevel level, LogLevel expected)
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration { Message = "m", Level = level });

        Assert.Equal(expected, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task Attributes_WithValueAndValuePath_AreEmittedInOrder()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            Message = "m",
            Attributes =
            [
                new LoggerNodeAttribute { Name = "MeteringPoint", ValuePath = "$.mp" },
                new LoggerNodeAttribute { Name = "Reason", Value = "UnknownMeteringPoint" },
                new LoggerNodeAttribute { Name = "Both", Value = "static", ValuePath = "$.mp" }
            ]
        }, """{"mp":"AT0010000000000000001000000000001"}""");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(["TenantId", "PipelineRtId", "NodePath", "MeteringPoint", "Reason", "Both"],
            entry.Attributes.Select(a => a.Key));
        Assert.Equal("AT0010000000000000001000000000001", Attribute(entry, "MeteringPoint"));
        Assert.Equal("UnknownMeteringPoint", Attribute(entry, "Reason"));
        // valuePath takes precedence over value, as in the SDK's value/valuePath convention.
        Assert.Equal("AT0010000000000000001000000000001", Attribute(entry, "Both"));
    }

    [Fact]
    public async Task LongMessage_IsTruncatedTo2KbWithMarker()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration { MessagePath = "$.text" },
            JsonSerializer.Serialize(new { text = new string('x', 5000) }));

        var message = Assert.Single(logger.Entries).Message;
        Assert.Equal(LoggerNodeConfiguration.MaxMessageLength, message.Length);
        Assert.EndsWith(LoggerNodeConfiguration.TruncationMarker, message);
    }

    [Fact]
    public async Task MessageAtTheLimit_IsNotTruncated()
    {
        var text = new string('x', LoggerNodeConfiguration.MaxMessageLength);
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration { Message = text });

        Assert.Equal(text, Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public async Task LongAttributeValue_IsTruncatedTo256WithMarker()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            Message = "m",
            Attributes =
            [
                new LoggerNodeAttribute { Name = "Static", Value = new string('s', 300) },
                new LoggerNodeAttribute { Name = "FromPath", ValuePath = "$.v" }
            ]
        }, JsonSerializer.Serialize(new { v = new string('p', 1000) }));

        var entry = Assert.Single(logger.Entries);
        foreach (var name in new[] { "Static", "FromPath" })
        {
            var value = Attribute(entry, name)!;
            Assert.Equal(LoggerNodeConfiguration.MaxAttributeValueLength, value.Length);
            Assert.EndsWith(LoggerNodeConfiguration.TruncationMarker, value);
        }
    }

    [Fact]
    public async Task TwentyAttributes_AreAllowed()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            Message = "m",
            Attributes = Enumerable.Range(1, LoggerNodeConfiguration.MaxAttributes)
                .Select(i => new LoggerNodeAttribute { Name = $"A{i}", Value = $"{i}" }).ToList()
        });

        Assert.Equal(LoggerNodeConfiguration.MaxAttributes + 3, Assert.Single(logger.Entries).Attributes.Count);
    }

    [Fact]
    public async Task RegisteredSecret_IsMaskedInMessageAndAttributes()
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            Attributes =
            [
                new LoggerNodeAttribute { Name = "Exact", ValuePath = "$.secret" },
                new LoggerNodeAttribute { Name = "Embedded", Value = $"before {Secret} after" }
            ]
        }, JsonSerializer.Serialize(new { text = $"login with {Secret}", secret = Secret }),
            ctx => ctx.RegisterSecret(Secret));

        var entry = Assert.Single(logger.Entries);
        const string mask = PipelineSecretRegistry.Mask;
        Assert.DoesNotContain(Secret, entry.Message);
        Assert.Equal($"login with {mask}", entry.Message);
        Assert.Equal(mask, Attribute(entry, "Exact"));
        Assert.Equal($"before {mask} after", Attribute(entry, "Embedded"));
    }

    [Fact]
    public async Task RegisteredSecretWithQuoteInsideAnObject_IsMaskedBeforeSerialization()
    {
        // JSON escaping turns the quote and the backslash into \" and \\, so a text redaction after
        // serialization would no longer find the secret.
        const string quotedSecret = "fake\"pass\\word-42";
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.cfg",
            Attributes = [new LoggerNodeAttribute { Name = "Cfg", ValuePath = "$.cfg" }]
        }, JsonSerializer.Serialize(new { cfg = new { user = "u", pw = quotedSecret, list = new[] { quotedSecret } } }),
            ctx => ctx.RegisterSecret(quotedSecret));

        var entry = Assert.Single(logger.Entries);
        const string expected = """{"user":"u","pw":"***","list":["***"]}""";
        Assert.Equal(expected, entry.Message);
        Assert.Equal(expected, Attribute(entry, "Cfg"));
    }

    [Fact]
    public async Task SecretAcrossTheCutOff_IsMaskedAsAWhole()
    {
        var text = new string('x', LoggerNodeConfiguration.MaxMessageLength - 20) + Secret + " tail";
        var value = new string('y', LoggerNodeConfiguration.MaxAttributeValueLength - 20) + Secret;
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            Attributes = [new LoggerNodeAttribute { Name = "V", ValuePath = "$.value" }]
        }, JsonSerializer.Serialize(new { text, value }), ctx => ctx.RegisterSecret(Secret));

        var entry = Assert.Single(logger.Entries);
        // Cutting first would leave a prefix of the secret that no later redaction recognises.
        Assert.DoesNotContain(Secret[..6], entry.Message);
        Assert.DoesNotContain(Secret[..6], Attribute(entry, "V"));
        Assert.Contains(PipelineSecretRegistry.Mask, entry.Message);
    }

    [Fact]
    public async Task Truncation_DoesNotSplitASurrogatePair()
    {
        // The cut falls between the two halves of the emoji.
        var keep = LoggerNodeConfiguration.MaxAttributeValueLength - LoggerNodeConfiguration.TruncationMarker.Length;
        var value = new string('a', keep - 1) + "\U0001F600" + new string('b', 50);
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            Message = "m",
            Attributes = [new LoggerNodeAttribute { Name = "V", Value = value }]
        });

        var truncated = Attribute(Assert.Single(logger.Entries), "V")!;
        Assert.Equal(new string('a', keep - 1) + LoggerNodeConfiguration.TruncationMarker, truncated);
    }

    [Theory]
    [InlineData("Value {0} and {name} and { unbalanced } } {")]
    [InlineData("{{escaped}} {0:N2} {-1}")]
    public async Task Braces_AreLoggedVerbatim_FromStaticMessageAndValues(string text)
    {
        var (logger, _, _) = await RunAsync(new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            Attributes = [new LoggerNodeAttribute { Name = "Text", Value = text }]
        }, JsonSerializer.Serialize(new { text }));
        var (staticLogger, _, _) = await RunAsync(new LoggerNodeConfiguration { Message = text });

        Assert.Equal(text, Assert.Single(logger.Entries).Message);
        Assert.Equal(text, Attribute(logger.Entries[0], "Text"));
        Assert.Equal(text, Assert.Single(staticLogger.Entries).Message);
    }

    public static TheoryData<LoggerNodeConfiguration, string> InvalidConfigurations => new()
    {
        { new LoggerNodeConfiguration(), "neither is set" },
        { new LoggerNodeConfiguration { MessagePath = "  " }, "neither is set" },
        { new LoggerNodeConfiguration { Message = "m", MessagePath = "$.x" }, "both are set" },
        { new LoggerNodeConfiguration { Message = "m", Level = (LoggerNodeLevel)42 }, "'level'" },
        {
            new LoggerNodeConfiguration
            {
                Message = "m",
                Attributes = Enumerable.Range(1, 21).Select(i => new LoggerNodeAttribute { Name = $"A{i}", Value = "v" })
                    .ToList()
            },
            "at most 20 attributes, but 21"
        },
        {
            new LoggerNodeConfiguration { Message = "m", Attributes = [new LoggerNodeAttribute { Name = " ", Value = "v" }] },
            "attribute 1 has no 'name'"
        },
        {
            new LoggerNodeConfiguration { Message = "m", Attributes = [new LoggerNodeAttribute { Name = "Id" }] },
            "'Id' needs 'value' or 'valuePath'"
        },
        {
            new LoggerNodeConfiguration
            {
                Message = "m",
                Attributes = [new LoggerNodeAttribute { Name = "Id", Value = "1" }, new LoggerNodeAttribute { Name = "id", Value = "2" }]
            },
            "'id' is used more than once"
        },
        {
            new LoggerNodeConfiguration { Message = "m", Attributes = [new LoggerNodeAttribute { Name = "tenantId", Value = "x" }] },
            "'tenantId' is reserved"
        },
        {
            new LoggerNodeConfiguration { Message = "m", Attributes = [new LoggerNodeAttribute { Name = "message", Value = "x" }] },
            "'message' is reserved"
        },
        {
            new LoggerNodeConfiguration
            {
                Message = "m", Attributes = [new LoggerNodeAttribute { Name = "{OriginalFormat}", Value = "{0}" }]
            },
            "'{OriginalFormat}' is reserved"
        }
    };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public async Task InvalidConfiguration_FailsWithAClearError(LoggerNodeConfiguration configuration, string expected)
    {
        var ex = await Assert.ThrowsAsync<PipelineConfigurationException>(() => RunAsync(configuration));

        Assert.Contains(expected, ex.Message);
        Assert.Contains("Logger@1", ex.Message);
        Assert.Contains(NodePath, ex.Message);
    }

    [Fact]
    public async Task DataContext_IsNotModified()
    {
        var logger = new RecordingPipelineLogger();
        using var dataContext = new DataContextImpl(JsonDocument.Parse("""{"sensor":"T1","temperature":23.5}"""));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = root.RegisterChildNode("Logger", 0, new LoggerNodeConfiguration
        {
            MessagePath = "$.sensor",
            Attributes = [new LoggerNodeAttribute { Name = "T", ValuePath = "$.temperature" }]
        }, dataContext);

        await new LoggerNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("T1", dataContext.Get<string>("$.sensor"));
        Assert.Equal(23.5, dataContext.Get<double>("$.temperature"));
    }
}

/// <summary>
/// Records the structured <see cref="IPipelineLogger.Log" /> calls; the other members are not
/// expected from Logger@1.
/// </summary>
internal sealed class RecordingPipelineLogger : IPipelineLogger
{
    public sealed record Entry(LogLevel Level, string NodePath, string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Attributes);

    public List<Entry> Entries { get; } = [];

    public List<string> OtherCalls { get; } = [];

    public void Log(LogLevel logLevel, string nodeId, string nodePath, string message,
        IReadOnlyList<KeyValuePair<string, object?>> attributes)
    {
        Entries.Add(new Entry(logLevel, nodePath, message, attributes.ToList()));
    }

    public void Debug(string nodeId, string nodePath, string message, params object[] args) => OtherCalls.Add(message);

    public void Info(string nodeId, string nodePath, string message, params object[] args) => OtherCalls.Add(message);

    public void Warning(string nodeId, string nodePath, string message, params object[] args) => OtherCalls.Add(message);

    public void Error(string nodeId, string nodePath, string message, params object[] args) => OtherCalls.Add(message);

    public void Error(string nodeId, string nodePath, Exception exception, string message, params object[] args) =>
        OtherCalls.Add(message);
}
