using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

public class ForNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(ForNodeConfiguration forNodeConfiguration,
        IPipelineDebugger? debugger = null,
        object? data = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = data ?? Generator.GenerateOrder();
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext, debugger);
        var nodeContext = rootNodeContext.RegisterChildNode("For", 0, forNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_OK()
    {
        ForNodeConfiguration forEachNodeConfiguration = new()
        {
            Count = 5,
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(5, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(5, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Debugger_InputCalls_OK()
    {
        ForNodeConfiguration forEachNodeConfiguration = new()
        {
            Count = 1,
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var debugger = A.Fake<IPipelineDebugger>();
        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration, debugger);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => debugger.LogInput(A<string>._, A<NodePath>._, A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappened(4, Times.Exactly);
        A.CallTo(() => debugger.LogInput(A<string>._, "PipelineExecution", A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => debugger.LogInput(A<string>._, "PipelineExecution/For", A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(
                () => debugger.LogInput(A<string>._, "PipelineExecution/For/[0]", A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() =>
                debugger.LogInput(A<string>._, "PipelineExecution/For/[0]/Test@1", A<string?>._, A<uint>._,
                    A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_Debugger_OutputCalls_OK()
    {
        ForNodeConfiguration forEachNodeConfiguration = new()
        {
            Count = 1,
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var debugger = A.Fake<IPipelineDebugger>();
        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration, debugger);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => debugger.LogOutput(A<string>._, A<NodePath>._, A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappened(2, Times.Exactly);
        A.CallTo(() =>
                debugger.LogOutput(A<string>._, "PipelineExecution/For/[0]", A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() =>
                debugger.LogOutput(A<string>._, "PipelineExecution/For/[0]/Test@1", A<string?>._, A<uint>._,
                    A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_Debugger_OutputCalls_MultipleElements_OK()
    {
        ForNodeConfiguration forEachNodeConfiguration = new()
        {
            Count = 1,
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestOutputNodeConfiguration(),
                new TestNodeConfiguration()
            }
        };

        var debugger = A.Fake<IPipelineDebugger>();
        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forEachNodeConfiguration, debugger);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => debugger.LogOutput(A<string>._, A<NodePath>._, A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() =>
                debugger.LogOutput(A<string>._, "PipelineExecution/For/[0]", A<string?>._, A<uint>._, A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() =>
                debugger.LogOutput(A<string>._, "PipelineExecution/For/[0]/TestOutput@1", A<string?>._, A<uint>._,
                    A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() =>
                debugger.LogOutput(A<string>._, "PipelineExecution/For/[0]/Test@1", A<string?>._, A<uint>._,
                    A<JsonNode?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task ProcessObjectAsync_WithMaxDegreeOfParallelism_AllItemsProcessed(int maxDop)
    {
        ForNodeConfiguration forNodeConfiguration = new()
        {
            Count = 5,
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = maxDop,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var (dataContext, nodeContext) = PrepareTest(forNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(5, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(5, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithCountPath_ResolvesCountFromDataContext()
    {
        ForNodeConfiguration forNodeConfiguration = new()
        {
            CountPath = "$.requestedCount",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var orderWithCount = (JsonSerializer.SerializeToNode(Generator.GenerateOrder(), SystemTextJsonOptions.Default) as JsonObject)!;
        orderWithCount["requestedCount"] = 3;
        var (dataContext, nodeContext) = PrepareTest(forNodeConfiguration, null, orderWithCount);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithCountPath_NullValue_ThrowsException()
    {
        ForNodeConfiguration forNodeConfiguration = new()
        {
            CountPath = "$.missingPath",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);

        var (dataContext, nodeContext) = PrepareTest(forNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithCountPathZero_ProducesEmptyResult()
    {
        ForNodeConfiguration forNodeConfiguration = new()
        {
            CountPath = "$.requestedCount",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);

        var orderWithCount = (JsonSerializer.SerializeToNode(Generator.GenerateOrder(), SystemTextJsonOptions.Default) as JsonObject)!;
        orderWithCount["requestedCount"] = 0;
        var (dataContext, nodeContext) = PrepareTest(forNodeConfiguration, null, orderWithCount);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        Assert.Equal(0, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_CountPathTakesPrecedenceOverCount()
    {
        ForNodeConfiguration forNodeConfiguration = new()
        {
            Count = 10,
            CountPath = "$.requestedCount",
            TargetPath = "$.Result",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration()
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(0);

        var orderWithCount = (JsonSerializer.SerializeToNode(Generator.GenerateOrder(), SystemTextJsonOptions.Default) as JsonObject)!;
        orderWithCount["requestedCount"] = 2;
        var (dataContext, nodeContext) = PrepareTest(forNodeConfiguration, null, orderWithCount);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(2, Times.Exactly);
        Assert.Equal(2, dataContext.Length("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MaxDegreeOfParallelism_LimitsConcurrency()
    {
        var concurrencyTracker = new ConcurrencyTracker();
        fixture.Services.AddSingleton(concurrencyTracker);

        ForNodeConfiguration forNodeConfiguration = new()
        {
            Count = 5,
            TargetPath = "$.Result",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration>
            {
                new DelayedTestNodeConfiguration
                {
                    TargetPath = "$.key",
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(forNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ForNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(1, concurrencyTracker.MaxConcurrent);
        Assert.Equal(5, concurrencyTracker.TotalExecutions);
        Assert.Equal(5, dataContext.Length("$.Result"));
    }
}
