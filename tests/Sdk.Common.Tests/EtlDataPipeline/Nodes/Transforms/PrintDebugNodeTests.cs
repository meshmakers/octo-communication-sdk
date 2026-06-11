using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class PrintDebugNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private static IDataContext MakeContext(JsonNode? data)
    {
        var json = data?.ToJsonString() ?? "null";
        return new DataContextImpl(JsonDocument.Parse(json));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithInformationSeverity_LogsInfo()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject { ["message"] = "Hello, World!" });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Information
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithDebugSeverity_LogsDebug()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject { ["value"] = 42 });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Debug
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithWarningSeverity_LogsWarning()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject { ["warning"] = "Something might be wrong" });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Warning
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithErrorSeverity_LogsError()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject { ["error"] = "An error occurred" });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Error
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithNullCurrent_LogsNull()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(null);

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Information
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithDefaultSeverity_UsesInformation()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject { ["data"] = "test" });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration(), dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithComplexObject_LogsStringRepresentation()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject
        {
            ["nested"] = new JsonObject
            {
                ["array"] = new JsonArray { 1, 2, 3 },
                ["value"] = "test"
            }
        });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Debug
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithPrimitiveValue_LogsValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("42"));

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Information
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithInvalidSeverity_ThrowsArgumentOutOfRangeException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject());

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = (LoggerSeverity)999
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithObjectRoot_LogsIndentedFormat()
    {
        // Legacy JObject.ToString() emitted Formatting.Indented by default. The STJ
        // migration accidentally produced compact output for object/array roots,
        // hurting debug-log readability. Verify the rendered message contains newlines
        // so debug logs remain useful.
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = MakeContext(new JsonObject
        {
            ["a"] = 1,
            ["b"] = new JsonObject { ["c"] = 2 }
        });

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("PrintDebug", 0, new PrintDebugNodeConfiguration
        {
            Severity = LoggerSeverity.Information
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new PrintDebugNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => logger.Info(
                A<string>._,
                A<string>._,
                A<string>.That.Matches(s => s.Contains('\n') && s.Contains("  "),
                    "indented multi-line"),
                A<object[]>._))
            .MustHaveHappenedOnceExactly();
    }
}
