using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Logger@1 (AB#9335) outside the node itself: YAML and JSON definitions, the published node schema,
/// a whole pipeline run (FormatString@1 into Logger@1), the additive interface members and the Studio
/// debug log.
/// </summary>
public class LoggerNodeConfigurationTests
{
    private const string ExampleYaml = """
                                       transformations:
                                         - type: SetPrimitiveValue@1
                                           targetPath: $.mp
                                           value: AT0010000000000000001000000000001
                                           valueType: String
                                         - type: FormatString@1
                                           format: "EDA_UNKNOWN_MP {$.mp} {0}"
                                           targetPath: $.logText
                                         - type: Logger@1
                                           messagePath: $.logText
                                           level: Warning
                                           missingValue: "-"
                                           attributes:
                                             - name: MeteringPoint
                                               valuePath: $.mp
                                             - name: Reason
                                               value: UnknownMeteringPoint
                                             - name: Absent
                                               valuePath: $.nope
                                         - type: Logger@1
                                           message: Unchanged static line
                                       """;

    private static IServiceProvider Services(IPipelineLogger? logger = null)
    {
        var fixture = new DataPipelineFixture();
        if (logger != null)
        {
            fixture.Services.Replace(ServiceDescriptor.Singleton(logger));
        }

        return fixture.Services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Warning", LoggerNodeLevel.Warning)]
    [InlineData("WARNING", LoggerNodeLevel.Warning)]
    [InlineData("error", LoggerNodeLevel.Error)]
    [InlineData("Debug", LoggerNodeLevel.Debug)]
    public async Task Yaml_NewProperties_AreDeserialized(string level, LoggerNodeLevel expected)
    {
        var yaml = $"""
                    transformations:
                      - type: Logger@1
                        messagePath: $.text
                        level: {level}
                        missingValue: n/a
                        attributes:
                          - name: Id
                            valuePath: $.id
                          - name: Kind
                            value: Static
                    """;

        var root = await Services().GetRequiredService<IPipelineConfigurationSerializer>().DeserializeAsync(yaml);

        var config = Assert.IsType<LoggerNodeConfiguration>(Assert.Single(root.Transformations!));
        Assert.Null(config.Message);
        Assert.Equal("$.text", config.MessagePath);
        Assert.Equal(expected, config.Level);
        Assert.Equal("n/a", config.MissingValue);
        Assert.Equal(2, config.Attributes!.Count);
        Assert.Equal(new LoggerNodeAttribute { Name = "Id", ValuePath = "$.id" }, config.Attributes[0]);
        Assert.Equal(new LoggerNodeAttribute { Name = "Kind", Value = "Static" }, config.Attributes[1]);
    }

    [Fact]
    public async Task Yaml_ExistingDefinition_StaysValid_WithDefaults()
    {
        const string yaml = """
                            transformations:
                              - type: Logger@1
                                message: hello
                            """;

        var root = await Services().GetRequiredService<IPipelineConfigurationSerializer>().DeserializeAsync(yaml);

        var config = Assert.IsType<LoggerNodeConfiguration>(Assert.Single(root.Transformations!));
        Assert.Equal("hello", config.Message);
        Assert.Null(config.MessagePath);
        Assert.Equal(LoggerNodeLevel.Information, config.Level);
        Assert.Null(config.Attributes);
        Assert.Null(config.MissingValue);
    }

    [Fact]
    public async Task Json_RoundTrip_KeepsTheNewProperties()
    {
        var serializer = Services().GetRequiredService<IJsonPipelineConfigurationSerializer>();
        var original = new LoggerNodeConfiguration
        {
            MessagePath = "$.text",
            Level = LoggerNodeLevel.Error,
            MissingValue = "-",
            Attributes = [new LoggerNodeAttribute { Name = "Id", ValuePath = "$.id" }]
        };

        var json = await serializer.SerializeAsync(new NodeDefinitionRoot { Transformations = [original] });
        var root = await serializer.DeserializeAsync(json);

        var copy = Assert.IsType<LoggerNodeConfiguration>(Assert.Single(root.Transformations!));
        Assert.Equal("$.text", copy.MessagePath);
        Assert.Equal(LoggerNodeLevel.Error, copy.Level);
        Assert.Equal("-", copy.MissingValue);
        Assert.Equal(original.Attributes[0], Assert.Single(copy.Attributes!));
    }

    [Fact]
    public void Schema_ContainsTheNewPropertiesWithDescriptions()
    {
        var registry = new NodeSchemaRegistry(new List<NodeLookup>(),
            new Dictionary<string, Type> { ["Logger@1"] = typeof(LoggerNodeConfiguration) });

        var descriptor = registry.GetDescriptor("Logger@1");

        Assert.NotNull(descriptor);
        Assert.Equal(1, descriptor.Version);
        var schema = JsonNode.Parse(descriptor.ConfigurationSchemaJson)!.AsObject();
        var properties = schema["properties"]!.AsObject();
        foreach (var name in new[] { "message", "messagePath", "level", "attributes", "missingValue" })
        {
            Assert.True(properties.ContainsKey(name), $"schema lacks '{name}'");
            Assert.False(string.IsNullOrWhiteSpace(properties[name]!["description"]?.GetValue<string>()),
                $"'{name}' has no description");
        }

        // Exactly one of message/messagePath is required, so neither is required on its own.
        var required = schema["required"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? [];
        Assert.DoesNotContain("message", required);
        Assert.DoesNotContain("messagePath", required);
        Assert.Equal("jsonpath", properties["messagePath"]!["x-input"]?.GetValue<string>());

        Assert.Equal(LoggerNodeConfiguration.MaxAttributes, properties["attributes"]!["maxItems"]?.GetValue<int>());

        var definitions = schema["definitions"]!.AsObject();
        var levels = definitions["LoggerNodeLevel"]!["enum"]!.AsArray().Select(e => e!.GetValue<string>()).ToList();
        Assert.Equal(["Debug", "DEBUG", "Information", "INFORMATION", "Warning", "WARNING", "Error", "ERROR"], levels);

        var attribute = definitions["LoggerNodeAttribute"]!.AsObject();
        Assert.Equal(["name"], attribute["required"]!.AsArray().Select(r => r!.GetValue<string>()));
        foreach (var name in new[] { "name", "value", "valuePath" })
        {
            Assert.False(string.IsNullOrWhiteSpace(attribute["properties"]![name]!["description"]?.GetValue<string>()),
                $"attribute '{name}' has no description");
        }
    }

    [Fact]
    public async Task Pipeline_FormatStringIntoLoggerWarning_LogsValuesAndAttributes_AndStaticLoggerUnchanged()
    {
        var logger = new RecordingPipelineLogger();
        var serviceProvider = Services(logger);
        var pipeline = await serviceProvider.GetRequiredService<IPipelineConfigurationSerializer>()
            .DeserializeAsync(ExampleYaml);
        var pipelineRtId = OctoObjectId.GenerateNewId();
        var orchestrator =
            new EtlDataOrchestrator(serviceProvider, serviceProvider.GetRequiredService<INodeLookupService>());

        // Typed as IEtlContext like AdapterTriggerContext, so a node can resolve the context.
        await orchestrator.ExecutePipelineAsync<IEtlContext>(pipeline,
            new DefaultEtlContext("logtenant", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
                new RtEntityId("System.Communication/Pipeline", pipelineRtId), DateTime.UtcNow, null,
                new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>()));

        Assert.Equal(2, logger.Entries.Count);
        var warning = logger.Entries[0];
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("EDA_UNKNOWN_MP AT0010000000000000001000000000001 {0}", warning.Message);
        var attributes = warning.Attributes.ToDictionary(a => a.Key, a => a.Value as string);
        Assert.Equal("logtenant", attributes["TenantId"]);
        Assert.Equal(pipelineRtId.ToString(), attributes["PipelineRtId"]);
        Assert.Equal(warning.NodePath, attributes["NodePath"]);
        Assert.Equal("AT0010000000000000001000000000001", attributes["MeteringPoint"]);
        Assert.Equal("UnknownMeteringPoint", attributes["Reason"]);
        Assert.Equal("-", attributes["Absent"]);

        var unchanged = logger.Entries[1];
        Assert.Equal(LogLevel.Information, unchanged.Level);
        Assert.Equal("Unchanged static line", unchanged.Message);
    }

    [Fact]
    public void PipelineLogger_DefaultLog_ForwardsTheMessageWithoutArguments()
    {
        var logger = A.Fake<IPipelineLogger>(o => o.CallsBaseMethods());

        logger.Log(LogLevel.Warning, "id", "path", "text {0}", [new("A", "1")]);

        A.CallTo(() => logger.Warning("id", "path", "text {0}", A<object[]>.That.IsEmpty()))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void NodeContext_DefaultLog_ForwardsTheMessageWithoutArguments()
    {
        var nodeContext = A.Fake<INodeContext>(o => o.CallsBaseMethods());

        nodeContext.Log(LogLevel.Error, "text {0}", []);

        A.CallTo(() => nodeContext.Error("text {0}", A<object[]>.That.IsEmpty())).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void DebugPipelineLogger_RecordsTheMessageVerbatim()
    {
        var logger = new DebugPipelineLogger(NullLoggerFactory.Instance);

        logger.Log(LogLevel.Warning, "0:Logger", "PipelineExecution/Logger", "value {0} {name} {", []);

        var message = Assert.Single(logger.Messages);
        Assert.Equal(LoggerSeverity.Warning, message.Severity);
        Assert.Equal("value {0} {name} {", message.Message);
    }
}
