using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Load;

public class ToPipelineDataEventNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private static readonly string TestTenantId = "test-tenant";
    private static readonly OctoObjectId TestDataFlowRtId = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId TestTargetPipelineId = OctoObjectId.GenerateNewId();

    [Fact]
    public async Task ProcessObjectAsync_SendsToExchange_CallsNext()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId
        };
        var testData = new { temperature = 42.5, sensor = "T1" };
        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
                A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .Returns(Task.FromResult(Task.CompletedTask));
        var etlContext = CreateEtlContext();
        var fn = A.Fake<NodeDelegate>();

        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
            A<string>._,
            A<string>._,
            A<PipelineDataReceived>._,
            A<CancellationToken?>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_SendsToCorrectExchangeAndRoutingKey()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        string? capturedExchangeName = null;
        string? capturedRoutingKey = null;

        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
                A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .Invokes((string exchangeName, string routingKey, PipelineDataReceived _, CancellationToken? _) =>
            {
                capturedExchangeName = exchangeName;
                capturedRoutingKey = routingKey;
            })
            .Returns(Task.FromResult(Task.CompletedTask));

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.NotNull(capturedExchangeName);
        Assert.Contains(TestTenantId, capturedExchangeName);
        Assert.Contains(TestDataFlowRtId.ToString()!.ToLower(), capturedExchangeName);
        Assert.StartsWith("octo::com::dataflow-", capturedExchangeName);
        Assert.Equal(TestTargetPipelineId.ToString(), capturedRoutingKey);
    }

    [Fact]
    public async Task ProcessObjectAsync_WithoutTargetPipelineRtId_ThrowsException()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = OctoObjectId.Empty
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        var fn = A.Fake<NodeDelegate>();

        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await Assert.ThrowsAsync<DataPipelineException>(() =>
            testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_SendsCorrectTenantAndPipelineIds()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        PipelineDataReceived? capturedMessage = null;

        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
                A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .Invokes((string _, string _, PipelineDataReceived msg, CancellationToken? _) => capturedMessage = msg)
            .Returns(Task.FromResult(Task.CompletedTask));

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.NotNull(capturedMessage);
        Assert.Equal(TestTenantId, capturedMessage.TenantId);
        Assert.Equal(TestDataFlowRtId, capturedMessage.DataFlowRtId);
    }

    [Fact]
    public async Task ProcessObjectAsync_NestedTargetPath_BuildsIntermediateObjects()
    {
        // Phase 2.5.2 walker collapse: ToPipelineDataEventNode's bespoke SetNestedValue / ParsePath
        // were replaced with JsonNodePath.Set. JsonNodePath.Set is dotted-only by design
        // (write paths cannot use brackets, wildcards, filters, or recursive descent), and
        // TargetPath is used exclusively as a write path here. There is therefore no
        // capability gain from this collapse — only structural deduplication of the walker
        // code. This test pins that the dotted semantics are preserved: a nested target path
        // creates the intermediate objects on the way in.
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$.outer.inner.payload",
            TargetPipelineRtId = TestTargetPipelineId
        };
        var testData = new { value = 42 };
        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        PipelineDataReceived? capturedMessage = null;

        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
                A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .Invokes((string _, string _, PipelineDataReceived msg, CancellationToken? _) => capturedMessage = msg)
            .Returns(Task.FromResult(Task.CompletedTask));

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.NotNull(capturedMessage);
        Assert.NotNull(capturedMessage.Value);
        var sentData = JsonNode.Parse(capturedMessage.Value)!;
        Assert.Equal(42, sentData["outer"]!["inner"]!["payload"]!["value"]!.GetValue<int>());
    }

    [Fact]
    public async Task ProcessObjectAsync_WithSubPath_SendsSubsetData()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$.nested",
            TargetPath = "$.output",
            TargetPipelineRtId = TestTargetPipelineId
        };
        var testData = new { nested = new { key = "value" }, other = "ignored" };
        var (dataContext, nodeContext) = PrepareTest(config, testData);
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        PipelineDataReceived? capturedMessage = null;

        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
                A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .Invokes((string _, string _, PipelineDataReceived msg, CancellationToken? _) => capturedMessage = msg)
            .Returns(Task.FromResult(Task.CompletedTask));

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.NotNull(capturedMessage);
        Assert.NotNull(capturedMessage.Value);

        var sentData = JsonNode.Parse(capturedMessage.Value)!;
        Assert.NotNull(sentData["output"]);
        Assert.Equal("value", sentData["output"]!["key"]?.GetValue<string>());

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_AwaitResultFalse_UsesPublishAndDoesNotCallCommandClient()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId,
            AwaitResult = false
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
                A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .Returns(Task.FromResult(Task.CompletedTask));
        var etlContext = CreateEtlContext();
        var fn = A.Fake<NodeDelegate>();

        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
            A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
            A<string>._, A<PipelineDataCommandRequest>._, A<CancellationToken>._, A<TimeSpan?>._))
            .MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_AwaitResultTrue_SendsCommandAndPlacesResult()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId,
            AwaitResult = true,
            ResultTargetPath = "$.pipelineResult"
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { input = "hello" });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        var fn = A.Fake<NodeDelegate>();

        var expectedResult = "{\"processed\":true,\"value\":42}";
        A.CallTo(() => distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
                A<string>._, A<PipelineDataCommandRequest>._, A<CancellationToken>._, A<TimeSpan?>._))
            .Returns(new PipelineDataCommandResponse { Success = true, Result = expectedResult });

        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
            A<string>._, A<PipelineDataCommandRequest>._, A<CancellationToken>._, A<TimeSpan?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => distributionEventHubService.SendToExchangeAsync(
            A<string>._, A<string>._, A<PipelineDataReceived>._, A<CancellationToken?>._))
            .MustNotHaveHappened();

        Assert.Equal(42, dataContext.Get<int>("$.pipelineResult.value"));
        Assert.True(dataContext.Get<bool>("$.pipelineResult.processed"));

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_AwaitResultTrue_SendsToCorrectCommandAddress()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId,
            AwaitResult = true
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        string? capturedAddress = null;

        A.CallTo(() => distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
                A<string>._, A<PipelineDataCommandRequest>._, A<CancellationToken>._, A<TimeSpan?>._))
            .Invokes((string addr, PipelineDataCommandRequest _, CancellationToken _, TimeSpan? _) =>
                capturedAddress = addr)
            .Returns(new PipelineDataCommandResponse { Success = true });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.NotNull(capturedAddress);
        var expectedAddress =
            $"pipelinedatacommand-{TestTenantId.ToLower()}-dataflow-{TestDataFlowRtId.ToString()!.ToLower()}-pipeline-{TestTargetPipelineId.ToString()!.ToLower()}";
        Assert.Equal(expectedAddress, capturedAddress);
    }

    [Fact]
    public async Task ProcessObjectAsync_AwaitResultTrue_TargetFails_ThrowsException()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId,
            AwaitResult = true
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();

        A.CallTo(() => distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
                A<string>._, A<PipelineDataCommandRequest>._, A<CancellationToken>._, A<TimeSpan?>._))
            .Returns(new PipelineDataCommandResponse
                { Success = false, ErrorMessage = "NullReferenceException in target" });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        var ex = await Assert.ThrowsAsync<DataPipelineException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));
        Assert.Contains("NullReferenceException in target", ex.Message);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_AwaitResultTrue_WithTimeoutSeconds_PassesTimeoutToGetResponse()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = TestTargetPipelineId,
            AwaitResult = true,
            TimeoutSeconds = 30
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        TimeSpan? capturedTimeout = null;

        A.CallTo(() => distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
                A<string>._, A<PipelineDataCommandRequest>._, A<CancellationToken>._, A<TimeSpan?>._))
            .Invokes((string _, PipelineDataCommandRequest _, CancellationToken _, TimeSpan? timeout) =>
                capturedTimeout = timeout)
            .Returns(new PipelineDataCommandResponse { Success = true });

        var fn = A.Fake<NodeDelegate>();
        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.NotNull(capturedTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), capturedTimeout.Value);
    }

    [Fact]
    public async Task ProcessObjectAsync_AwaitResultTrue_WithoutTargetPipelineRtId_ThrowsException()
    {
        var config = new ToPipelineDataEventNodeConfiguration
        {
            Path = "$",
            TargetPath = "$",
            TargetPipelineRtId = OctoObjectId.Empty,
            AwaitResult = true
        };
        var (dataContext, nodeContext) = PrepareTest(config, new { value = 1 });
        var distributionEventHubService = A.Fake<IDistributionEventHubService>();
        var etlContext = CreateEtlContext();
        var fn = A.Fake<NodeDelegate>();

        var testee = new ToPipelineDataEventNode(fn, etlContext, distributionEventHubService);

        await Assert.ThrowsAsync<DataPipelineException>(() =>
            testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    private (IDataContext, INodeContext) PrepareTest(ToPipelineDataEventNodeConfiguration config,
        object? data = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = data ?? new { };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext =
            rootNodeContext.RegisterChildNode("ToPipelineDataEvent", 0, config, dataContext);

        return (dataContext, nodeContext);
    }

    private static IEtlContext CreateEtlContext()
    {
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.TenantId).Returns(TestTenantId);
        A.CallTo(() => etlContext.DataFlowRtId).Returns(TestDataFlowRtId);
        A.CallTo(() => etlContext.PipelineRtEntityId).Returns(default(RtEntityId));
        A.CallTo(() => etlContext.TransactionStartedDateTime).Returns(DateTime.UtcNow);
        return etlContext;
    }
}
