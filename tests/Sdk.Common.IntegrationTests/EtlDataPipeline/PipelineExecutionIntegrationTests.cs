using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Sdk.Common.IntegrationTests.Fixtures;

namespace Sdk.Common.IntegrationTests.EtlDataPipeline;

[Trait("Category", "Integration")]
public class PipelineExecutionIntegrationTests(IntegrationTestFixture fixture)
    : IClassFixture<IntegrationTestFixture>
{
    [Fact]
    public async Task ExecutePipeline_EmptyPipeline_ReturnsInputData()
    {
        // Arrange
        var orchestrator = fixture.CreateOrchestrator();
        var context = fixture.CreateContext();
        var pipelineConfig = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>()
        };
        var inputData = new { value = 42 };

        // Act
        var result = await orchestrator.ExecutePipelineAsync(pipelineConfig, context, value: inputData);

        // Assert
        Assert.NotNull(result);
        var jsonObject = result as JsonObject;
        Assert.NotNull(jsonObject);
        Assert.Equal(42, jsonObject["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecutePipeline_WithLinearScalerNode_ScalesValue()
    {
        // Arrange
        var orchestrator = fixture.CreateOrchestrator();
        var context = fixture.CreateContext();

        var pipelineConfig = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new LinearScalerNodeConfiguration
                {
                    Path = "$.temperature",
                    TargetPath = "$.temperatureScaled",
                    ScaleInputMin = 0,
                    ScaleInputMax = 100,
                    ScaleOutputMin = 32,
                    ScaleOutputMax = 212 // Celsius to Fahrenheit
                }
            }
        };

        // 0°C should become 32°F
        var inputData = new { temperature = 0.0 };

        // Act
        var result = await orchestrator.ExecutePipelineAsync(pipelineConfig, context, value: inputData);

        // Assert
        Assert.NotNull(result);
        var jsonObject = result as JsonObject;
        Assert.NotNull(jsonObject);
        Assert.Equal(32.0, jsonObject["temperatureScaled"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecutePipeline_WithLinearScalerNode_ScalesValue_AtMax()
    {
        // Arrange
        var orchestrator = fixture.CreateOrchestrator();
        var context = fixture.CreateContext();

        var pipelineConfig = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new LinearScalerNodeConfiguration
                {
                    Path = "$.temperature",
                    TargetPath = "$.temperatureScaled",
                    ScaleInputMin = 0,
                    ScaleInputMax = 100,
                    ScaleOutputMin = 32,
                    ScaleOutputMax = 212 // Celsius to Fahrenheit
                }
            }
        };

        // 100°C should become 212°F
        var inputData = new { temperature = 100.0 };

        // Act
        var result = await orchestrator.ExecutePipelineAsync(pipelineConfig, context, value: inputData);

        // Assert
        Assert.NotNull(result);
        var jsonObject = result as JsonObject;
        Assert.NotNull(jsonObject);
        Assert.Equal(212.0, jsonObject["temperatureScaled"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecutePipeline_WithMultipleScalerNodes_ExecutesInSequence()
    {
        // Arrange
        var orchestrator = fixture.CreateOrchestrator();
        var context = fixture.CreateContext();

        var pipelineConfig = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new LinearScalerNodeConfiguration
                {
                    Path = "$.value",
                    TargetPath = "$.doubled",
                    ScaleInputMin = 0,
                    ScaleInputMax = 100,
                    ScaleOutputMin = 0,
                    ScaleOutputMax = 200
                },
                new LinearScalerNodeConfiguration
                {
                    Path = "$.doubled",
                    TargetPath = "$.quadrupled",
                    ScaleInputMin = 0,
                    ScaleInputMax = 200,
                    ScaleOutputMin = 0,
                    ScaleOutputMax = 400
                }
            }
        };

        var inputData = new { value = 10.0 };

        // Act
        var result = await orchestrator.ExecutePipelineAsync(pipelineConfig, context, value: inputData);

        // Assert
        Assert.NotNull(result);
        var jsonObject = result as JsonObject;
        Assert.NotNull(jsonObject);
        Assert.Equal(10.0, jsonObject["value"]?.GetValue<double>());
        Assert.Equal(20.0, jsonObject["doubled"]?.GetValue<double>());
        Assert.Equal(40.0, jsonObject["quadrupled"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecutePipeline_WithProjectNode_ExcludesSpecifiedFields()
    {
        // Arrange
        var orchestrator = fixture.CreateOrchestrator();
        var context = fixture.CreateContext();

        var pipelineConfig = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration>
            {
                new ProjectNodeConfiguration
                {
                    Fields = new List<FieldConfiguration>
                    {
                        new() { Path = "$.secret" },
                        new() { Path = "$.password" }
                    }
                }
            }
        };

        var inputData = new
        {
            name = "Test",
            value = 123,
            secret = "should-be-excluded",
            password = "also-excluded"
        };

        // Act
        var result = await orchestrator.ExecutePipelineAsync(pipelineConfig, context, value: inputData);

        // Assert
        Assert.NotNull(result);
        var jsonObject = result as JsonObject;
        Assert.NotNull(jsonObject);
        Assert.Equal("Test", jsonObject["name"]?.GetValue<string>());
        Assert.Equal(123, jsonObject["value"]?.GetValue<int>());
        Assert.Null(jsonObject["secret"]);
        Assert.Null(jsonObject["password"]);
    }
}
