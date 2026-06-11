using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class FormatStringNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(FormatStringNodeConfiguration configuration, JsonObject? testData = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var data = testData ?? new JsonObject
        {
            ["user"] = new JsonObject
            {
                ["name"] = "John Doe",
                ["age"] = 30,
                ["email"] = "john@example.com"
            },
            ["items"] = new JsonArray { "apple", "banana", "orange" },
            ["count"] = 5,
            ["isActive"] = true,
            ["nullField"] = null
        };
        var dataContext = new DataContextImpl(JsonDocument.Parse(data.ToJsonString()));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("FormatString", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_SimpleFormat_OK()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "Hello {$.user.name}, you have {$.count} items",
            TargetPath = "$.message"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello John Doe, you have 5 items", dataContext.Get<string>("$.message"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MultipleJsonPaths_OK()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "User {$.user.name} ({$.user.email}) is {$.user.age} years old and has {$.count} items",
            TargetPath = "$.formattedUser"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("User John Doe (john@example.com) is 30 years old and has 5 items",
            dataContext.Get<string>("$.formattedUser"));
    }

    // TODO Phase 11 regression: FormatStringNode uses dataContext.Get<JsonNode>(path)
    // which returns null both when the path is missing AND when it contains a JSON null.
    // The node treats both as "path not found" and throws. The legacy Newtonsoft pipeline
    // distinguished these cases. Flag for follow-up; node needs to use Exists()/GetKind()
    // to disambiguate.
    [Fact]
    public async Task ProcessObjectAsync_NullValue_UsesNullString()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "Field value: {$.nullField}",
            TargetPath = "$.result",
            NullValue = "NULL"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Field value: NULL", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_CustomNullValue_OK()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "Status: {$.nullField}",
            TargetPath = "$.status",
            NullValue = "N/A"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);
        Assert.Equal("Status: N/A", dataContext.Get<string>("$.status"));
    }

    [Fact]
    public async Task ProcessObjectAsync_BooleanValue_OK()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "User is active: {$.isActive}",
            TargetPath = "$.activeStatus"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Legacy Newtonsoft JToken.ToString() rendered booleans capitalized
        // ("True"/"False"). We route stringification through JsonStringifyHelper
        // to preserve that legacy parity (hash-stability critical for HashNode).
        Assert.Equal("User is active: True", dataContext.Get<string>("$.activeStatus"));
    }

    [Fact]
    public async Task FormatStringNode_BooleanValue_RendersCapitalized()
    {
        // Pre-fix: JsonNode.ToJsonString() rendered "true" (lowercase).
        // Post-fix: JsonStringifyHelper.ToLegacyString() preserves the legacy
        // Newtonsoft behaviour ("True"/"False") for hash-stability and
        // FormatString/Concat output parity.
        var data = new JsonObject
        {
            ["flag"] = true,
            ["other"] = false
        };
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "{$.flag}/{$.other}",
            TargetPath = "$.result"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, data);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var result = dataContext.Get<string>("$.result");
        Assert.Equal("True/False", result);
        Assert.DoesNotContain("true", result);
        Assert.DoesNotContain("false", result);
    }

    [Fact]
    public async Task ProcessObjectAsync_PathNotFound_ThrowsException()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "Value: {$.nonexistent.path}",
            TargetPath = "$.result"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(
            async () => await testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_ArrayPath_ThrowsException()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "Items: {$.items}",
            TargetPath = "$.result"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(
            async () => await testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_ObjectPath_ThrowsException()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "User: {$.user}",
            TargetPath = "$.result"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(
            async () => await testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoPlaceholders_ReturnsOriginalFormat()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "This is a static message with no placeholders",
            TargetPath = "$.staticMessage"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("This is a static message with no placeholders",
            dataContext.Get<string>("$.staticMessage"));
    }

    [Fact]
    public async Task ProcessObjectAsync_RepeatedPlaceholder_OK()
    {
        var configuration = new FormatStringNodeConfiguration
        {
            Format = "{$.user.name} - {$.user.name} has {$.count} items",
            TargetPath = "$.repeated"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new FormatStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("John Doe - John Doe has 5 items",
            dataContext.Get<string>("$.repeated"));
    }
}
