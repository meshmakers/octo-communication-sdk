using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538: type-switch nodes refuse Secrets explicitly - as configured value type and as the
/// <c>{"isSet": …}</c> marker an entity's Secret attribute reads as.
/// </summary>
public class SecretValueGuardTests(ServiceCollectionFixture fixture) : IClassFixture<ServiceCollectionFixture>
{
    private const string EntityJson = """{"attributes":{"password":{"isSet":true},"name":"svc"}}""";

    private (DataContextImpl DataContext, INodeContext NodeContext) Create(INodeConfiguration configuration)
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse(EntityJson));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext);
        return (dataContext, root.RegisterChildNode("Node", 0, configuration, dataContext));
    }

    private static void AssertSecretNotSupported(Exception e)
    {
        var pe = Assert.IsType<PipelineExecutionException>(e);
        Assert.Contains("Secret not supported", pe.Message);
    }

    [Theory]
    [InlineData("""{"isSet":true}""", true)]
    [InlineData("""{"isSet":false}""", true)]
    [InlineData("""{"isSet":"yes"}""", false)]
    [InlineData("""{"isSet":true,"other":1}""", false)]
    [InlineData("""{"IsSet":true}""", false)]
    [InlineData("""[true]""", false)]
    [InlineData("\"text\"", false)]
    public void IsSecretMarker_RecognisesOnlyTheMarkerShape(string json, bool expected)
    {
        Assert.Equal(expected, PipelineSecretValues.IsSecretMarker(JsonNode.Parse(json)));
    }

    [Fact]
    public async Task ConvertDataType_SecretValueType_Throws()
    {
        var (dc, nc) = Create(new ConvertDataTypeNodeConfiguration
            { Path = "$.attributes.name", TargetPath = "$.x", ValueType = AttributeValueTypesDto.Secret });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new ConvertDataTypeNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task ConvertDataType_SecretMarker_Throws()
    {
        var (dc, nc) = Create(new ConvertDataTypeNodeConfiguration
            { Path = "$.attributes.password", TargetPath = "$.x", ValueType = AttributeValueTypesDto.String });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new ConvertDataTypeNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task ConvertDataType_MarkerIsSet_StaysUsable()
    {
        var (dc, nc) = Create(new ConvertDataTypeNodeConfiguration
            { Path = "$.attributes.password.isSet", TargetPath = "$.x", ValueType = AttributeValueTypesDto.String });

        await new ConvertDataTypeNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc);

        Assert.Equal("true", dc.Get<string>("$.x"));
    }

    [Fact]
    public async Task SetPrimitiveValue_SecretValueType_Throws()
    {
        var (dc, nc) = Create(new SetPrimitiveValueNodeConfiguration
            { TargetPath = "$.x", Value = "fake", ValueType = AttributeValueTypesDto.Secret });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new SetPrimitiveValueNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task SetPrimitiveValue_SecretMarkerAtValuePath_Throws()
    {
        var (dc, nc) = Create(new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.x", ValuePath = "$.attributes.password", ValueType = AttributeValueTypesDto.String
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new SetPrimitiveValueNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task If_SecretMarkerAtPath_Throws()
    {
        var (dc, nc) = Create(new IfNodeConfiguration
        {
            Path = "$.attributes.password", Value = "x", ValueType = AttributeValueTypesDto.String,
            Transformations = []
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new IfNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task If_SecretValueType_Throws()
    {
        var (dc, nc) = Create(new IfNodeConfiguration
        {
            Path = "$.attributes.name", Value = "x", ValueType = AttributeValueTypesDto.Secret,
            Transformations = []
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new IfNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task If_OnMarkerIsSet_StaysUsable()
    {
        var (dc, nc) = Create(new IfNodeConfiguration
        {
            // Condition not met (isSet is true), so no child pipeline is needed - the point is that
            // reading isSet as Boolean is not refused.
            Path = "$.attributes.password.isSet", Value = false, ValueType = AttributeValueTypesDto.Boolean,
            Transformations = []
        });
        var next = A.Fake<NodeDelegate>();

        await new IfNode(next).ProcessObjectAsync(dc, nc);

        A.CallTo(() => next.Invoke(dc, nc)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Switch_SecretMarkerAtPath_Throws()
    {
        var (dc, nc) = Create(new SwitchNodeConfiguration
        {
            Path = "$.attributes.password", ValueType = AttributeValueTypesDto.String, Cases = []
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new SwitchNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task Switch_SecretValueType_Throws()
    {
        var (dc, nc) = Create(new SwitchNodeConfiguration
        {
            Path = "$.attributes.name", ValueType = AttributeValueTypesDto.Secret, Cases = []
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new SwitchNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task ExecuteCSharp_SecretMarkerArgument_Throws()
    {
        var (dc, nc) = Create(new ExecuteCSharpNodeConfiguration
        {
            TargetPath = "$.x", Code = "return 1;", ReturnType = AttributeValueTypesDto.Int,
            Arguments = [new ScriptArgument
                { Name = "p", ValuePath = "$.attributes.password", DataType = AttributeValueTypesDto.String }]
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new ExecuteCSharpNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task ExecuteCSharp_SecretArgumentType_Throws()
    {
        var (dc, nc) = Create(new ExecuteCSharpNodeConfiguration
        {
            TargetPath = "$.x", Code = "return 1;", ReturnType = AttributeValueTypesDto.Int,
            Arguments = [new ScriptArgument
                { Name = "p", ValuePath = "$.attributes.name", DataType = AttributeValueTypesDto.Secret }]
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new ExecuteCSharpNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }

    [Fact]
    public async Task ExecuteCSharp_SecretReturnType_Throws()
    {
        var (dc, nc) = Create(new ExecuteCSharpNodeConfiguration
        {
            TargetPath = "$.x", Code = "return \"x\";", ReturnType = AttributeValueTypesDto.Secret
        });

        AssertSecretNotSupported(await Assert.ThrowsAnyAsync<Exception>(() =>
            new ExecuteCSharpNode(A.Fake<NodeDelegate>()).ProcessObjectAsync(dc, nc)));
    }
}
