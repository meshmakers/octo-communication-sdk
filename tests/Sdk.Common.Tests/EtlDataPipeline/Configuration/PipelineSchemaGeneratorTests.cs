using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration;

public class PipelineSchemaGeneratorTests
{
    private readonly INodeSchemaRegistry _registry = A.Fake<INodeSchemaRegistry>();

    private PipelineSchemaGenerator CreateGenerator() => new(_registry);

    [Fact]
    public void GenerateSchema_EmptyRegistry_ReturnsValidSchema()
    {
        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>());

        var generator = CreateGenerator();
        var json = generator.GenerateSchema();
        var schema = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]?.GetValue<string>());
        Assert.Equal("object", schema["type"]?.GetValue<string>());
        Assert.NotNull(schema["properties"]?["triggers"]);
        Assert.NotNull(schema["properties"]?["transformations"]);
        Assert.NotNull(schema["$defs"]?["TriggerNode"]);
        Assert.NotNull(schema["$defs"]?["TransformationNode"]);
    }

    [Fact]
    public void GenerateSchema_WithTriggerAndTransformation_CreatesOneOfArrays()
    {
        var triggerSchema = """{"type":"object","properties":{"interval":{"type":"integer"}}}""";
        var transformSchema = """{"type":"object","properties":{"path":{"type":"string"}}}""";

        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Polling", 1, "Trigger", true, false, triggerSchema),
            new("Select", 1, "Transform", false, false, transformSchema),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var triggerNode = schema["$defs"]?["TriggerNode"];
        Assert.NotNull(triggerNode?["oneOf"]);
        Assert.Single(triggerNode!["oneOf"]!.AsArray());

        var transformationNode = schema["$defs"]?["TransformationNode"];
        Assert.NotNull(transformationNode?["oneOf"]);
        Assert.Single(transformationNode!["oneOf"]!.AsArray());
    }

    [Fact]
    public void GenerateSchema_InjectsTypeDiscriminator()
    {
        var nodeSchema = """{"type":"object","properties":{"path":{"type":"string"}}}""";

        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Select", 2, "Transform", false, false, nodeSchema),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var nodeOneOf = schema["$defs"]?["TransformationNode"]?["oneOf"]?[0];
        Assert.NotNull(nodeOneOf);

        var typeProp = nodeOneOf!["properties"]?["type"];
        Assert.Equal("string", typeProp?["type"]?.GetValue<string>());
        Assert.Equal("Select@2", typeProp?["const"]?.GetValue<string>());

        var required = nodeOneOf["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Contains("type", required.Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public void GenerateSchema_ChildCapableNode_AddsTransformationsProperty()
    {
        var nodeSchema = """{"type":"object","properties":{"condition":{"type":"string"}}}""";

        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("If", 1, "Control", false, true, nodeSchema),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var nodeOneOf = schema["$defs"]?["TransformationNode"]?["oneOf"]?[0];
        var transformationsProp = nodeOneOf?["properties"]?["transformations"];
        Assert.NotNull(transformationsProp);
        Assert.Equal("array", transformationsProp!["type"]?.GetValue<string>());
        Assert.Equal("#/$defs/TransformationNode", transformationsProp["items"]?["$ref"]?.GetValue<string>());
    }

    [Fact]
    public void GenerateSchema_HoistsNestedDefs()
    {
        var nodeSchema = """
        {
            "type":"object",
            "properties":{
                "items":{"$ref":"#/$defs/SubItem"}
            },
            "$defs":{
                "SubItem":{"type":"object","properties":{"name":{"type":"string"}}}
            }
        }
        """;

        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Custom", 1, "Transform", false, false, nodeSchema),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        Assert.NotNull(schema["$defs"]?["Custom@1_SubItem"]);

        var nodeOneOf = schema["$defs"]?["TransformationNode"]?["oneOf"]?[0];
        Assert.Null(nodeOneOf?["$defs"]);

        var itemsRef = nodeOneOf?["properties"]?["items"]?["$ref"]?.GetValue<string>();
        Assert.Equal("#/$defs/Custom@1_SubItem", itemsRef);
    }

    [Fact]
    public void GenerateSchema_MultipleTriggers_AllIncludedInOneOf()
    {
        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Polling", 1, "Trigger", true, false, """{"type":"object","properties":{}}"""),
            new("Event", 1, "Trigger", true, false, """{"type":"object","properties":{}}"""),
            new("Cron", 2, "Trigger", true, false, """{"type":"object","properties":{}}"""),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var triggerOneOf = schema["$defs"]?["TriggerNode"]?["oneOf"] as JsonArray;
        Assert.NotNull(triggerOneOf);
        Assert.Equal(3, triggerOneOf!.Count);
    }

    [Fact]
    public void GenerateSchema_IsCached()
    {
        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>());

        var generator = CreateGenerator();
        var first = generator.GenerateSchema();
        var second = generator.GenerateSchema();

        Assert.Same(first, second);
        A.CallTo(() => _registry.GetAllDescriptors()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void GenerateSchema_InvalidSchemaJson_FallsBackToEmptyObject()
    {
        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Bad", 1, "Transform", false, false, "not-json"),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var nodeOneOf = schema["$defs"]?["TransformationNode"]?["oneOf"]?[0];
        Assert.NotNull(nodeOneOf);
        Assert.Equal("object", nodeOneOf!["type"]?.GetValue<string>());
        Assert.Equal("Bad@1", nodeOneOf["properties"]?["type"]?["const"]?.GetValue<string>());
    }

    [Fact]
    public void GenerateSchema_RemovesTopLevelSchemaProperties()
    {
        var nodeSchema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","$id":"test","type":"object","properties":{}}""";

        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Clean", 1, "Transform", false, false, nodeSchema),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var nodeOneOf = schema["$defs"]?["TransformationNode"]?["oneOf"]?[0];
        Assert.Null(nodeOneOf?["$schema"]);
        Assert.Null(nodeOneOf?["$id"]);
    }

    [Fact]
    public void GenerateSchema_PreservesExistingRequired()
    {
        var nodeSchema = """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";

        A.CallTo(() => _registry.GetAllDescriptors()).Returns(new List<NodeDescriptor>
        {
            new("Select", 1, "Transform", false, false, nodeSchema),
        });

        var generator = CreateGenerator();
        var schema = JsonNode.Parse(generator.GenerateSchema())!.AsObject();

        var nodeOneOf = schema["$defs"]?["TransformationNode"]?["oneOf"]?[0];
        var required = nodeOneOf?["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Contains("path", required!.Select(r => r!.GetValue<string>()));
        Assert.Contains("type", required.Select(r => r!.GetValue<string>()));
    }
}
