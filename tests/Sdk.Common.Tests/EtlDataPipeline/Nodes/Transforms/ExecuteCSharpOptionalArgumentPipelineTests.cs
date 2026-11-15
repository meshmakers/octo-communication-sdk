using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// AB#9006 end to end: the orchestrator reports a missing <c>ExecuteCSharp@1</c> argument path once
/// per execution, the YAML deserializer reads <c>optional</c>/<c>defaultValue</c>, existing YAML
/// without them still deserializes, and the generated pipeline schema exposes both fields.
/// </summary>
public class ExecuteCSharpOptionalArgumentPipelineTests
{
    private static NodeDefinitionRoot BuildPipeline(int recordCount, bool optional)
    {
        var items = new JsonArray();
        for (var i = 0; i < recordCount; i++)
        {
            items.Add(new JsonObject { ["id"] = i });
        }

        return new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new SetJsonNodeConfiguration { TargetPath = "$.items", JsonString = items.ToJsonString() },
                new ForEachNodeConfiguration
                {
                    Path = "$",
                    IterationPath = "$.items",
                    TargetPath = "$.results",
                    MergePath = "$.key",
                    MaxDegreeOfParallelism = 4,
                    Transformations = new List<NodeConfiguration>
                    {
                        new ExecuteCSharpNodeConfiguration
                        {
                            Code = "dueDate == null ? \"none\" : dueDate",
                            Arguments = new List<ScriptArgument>
                            {
                                new()
                                {
                                    Name = "dueDate", ValuePath = "$.key.dueDate",
                                    DataType = AttributeValueTypesDto.String, Optional = optional ? true : null
                                }
                            },
                            ReturnType = AttributeValueTypesDto.String,
                            TargetPath = "$.key.result"
                        }
                    }
                }
            }
        };
    }

    private static async Task<(JsonNode? Result, IPipelineLogger Logger)> ExecuteAsync(NodeDefinitionRoot pipeline)
    {
        var fixture = new DataPipelineFixture();
        var logger = A.Fake<IPipelineLogger>();
        fixture.Services.Replace(ServiceDescriptor.Singleton(logger));
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider, serviceProvider.GetRequiredService<INodeLookupService>());

        var result = await orchestrator.ExecutePipelineAsync(pipeline,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
                new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId()), DateTime.UtcNow, null,
                new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>()));
        return ((JsonNode?)result, logger);
    }

    private static bool IsAggregatedMissingWarning(string message) =>
        message.StartsWith("ExecuteCSharp argument '{ArgumentName}'", StringComparison.Ordinal);

    [Fact]
    public async Task ExecutePipelineAsync_NonOptionalArgumentMissingIn1000Records_OneWarningPerExecution()
    {
        var (result, logger) = await ExecuteAsync(BuildPipeline(1000, optional: false));

        Assert.Equal("none", result!["results"]![999]!["result"]!.GetValue<string>());
        A.CallTo(() => logger.Warning(A<string>._, A<string>._, A<string>.That.Matches(m => IsAggregatedMissingWarning(m)),
                A<object[]>.That.Matches(a => (long)a[2] == 1000L)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => logger.Warning(A<string>._, A<string>._, A<string>.That.Contains("not found for argument"), A<object[]>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ExecutePipelineAsync_SecondExecution_ReportsItsOwnCount()
    {
        var pipeline = BuildPipeline(5, optional: false);
        await ExecuteAsync(pipeline);
        var (_, logger) = await ExecuteAsync(pipeline);

        A.CallTo(() => logger.Warning(A<string>._, A<string>._, A<string>.That.Matches(m => IsAggregatedMissingWarning(m)),
                A<object[]>.That.Matches(a => (long)a[2] == 5L)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ExecutePipelineAsync_OptionalArgumentMissingIn1000Records_NoWarning()
    {
        var (result, logger) = await ExecuteAsync(BuildPipeline(1000, optional: true));

        Assert.Equal("none", result!["results"]![0]!["result"]!.GetValue<string>());
        A.CallTo(() => logger.Warning(A<string>._, A<string>._, A<string>._, A<object[]>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ExecutePipelineAsync_FailingExecution_StillReportsTheAggregatedWarning()
    {
        var pipeline = BuildPipeline(3, optional: false);
        pipeline.Transformations!.Add(new ExecuteCSharpNodeConfiguration
        {
            Code = "throw new System.InvalidOperationException(\"boom\")",
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.never"
        });

        // The aggregated warning is emitted from the orchestrator's finally block, so it is
        // logged although a later node fails the execution.
        var fixture = new DataPipelineFixture();
        var logger = A.Fake<IPipelineLogger>();
        fixture.Services.Replace(ServiceDescriptor.Singleton(logger));
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider, serviceProvider.GetRequiredService<INodeLookupService>());
        await Assert.ThrowsAnyAsync<Exception>(() => orchestrator.ExecutePipelineAsync(pipeline,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
                new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId()), DateTime.UtcNow, null,
                new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>())));

        A.CallTo(() => logger.Warning(A<string>._, A<string>._, A<string>.That.Matches(m => IsAggregatedMissingWarning(m)),
                A<object[]>.That.Matches(a => (long)a[2] == 3L)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Yaml_OptionalAndDefaultValue_AreDeserialized_AndOldDefinitionsStayValid()
    {
        const string yaml = """
                            transformations:
                              - type: ExecuteCSharp@1
                                code: a + b.Length
                                returnType: Int
                                targetPath: $.result
                                arguments:
                                  - name: a
                                    valuePath: $.a
                                    dataType: Int
                                    optional: true
                                    defaultValue: 42
                                  - name: b
                                    valuePath: $.b
                                    dataType: String
                                  - name: c
                                    valuePath: $.c
                                    dataType: String
                                    optional:
                            """;
        var serializer = new DataPipelineFixture().Services.BuildServiceProvider()
            .GetRequiredService<IPipelineConfigurationSerializer>();

        var root = await serializer.DeserializeAsync(yaml);

        var config = Assert.IsType<ExecuteCSharpNodeConfiguration>(Assert.Single(root.Transformations!));
        var args = config.Arguments.ToList();
        Assert.True(args[0].Optional);
        Assert.Equal("42", Convert.ToString(args[0].DefaultValue, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(args[1].Optional);
        Assert.Null(args[1].DefaultValue);
        Assert.Null(args[2].Optional);
    }

    [Fact]
    public void Schema_ScriptArgument_ExposesOptionalAndDefaultValue_WithoutMakingThemRequired()
    {
        var registry = new NodeSchemaRegistry(new List<NodeLookup>(),
            new Dictionary<string, Type> { ["ExecuteCSharp@1"] = typeof(ExecuteCSharpNodeConfiguration) });
        var descriptor = registry.GetDescriptor("ExecuteCSharp@1");
        Assert.NotNull(descriptor);
        var schema = JsonNode.Parse(descriptor.ConfigurationSchemaJson)!;

        var argumentSchema = FindObjectWithProperty(schema, "valuePath");
        Assert.NotNull(argumentSchema);
        var properties = argumentSchema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("optional"));
        Assert.True(properties.ContainsKey("defaultValue"));
        Assert.Contains("boolean", properties["optional"]!.ToJsonString());

        var required = argumentSchema["required"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? [];
        Assert.DoesNotContain("optional", required);
        Assert.DoesNotContain("defaultValue", required);
    }

    private static JsonObject? FindObjectWithProperty(JsonNode? node, string propertyName)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject props && props.ContainsKey(propertyName))
                {
                    return obj;
                }

                foreach (var (_, child) in obj)
                {
                    var found = FindObjectWithProperty(child, propertyName);
                    if (found != null) return found;
                }

                return null;
            case JsonArray array:
                foreach (var child in array)
                {
                    var found = FindObjectWithProperty(child, propertyName);
                    if (found != null) return found;
                }

                return null;
            default:
                return null;
        }
    }
}
