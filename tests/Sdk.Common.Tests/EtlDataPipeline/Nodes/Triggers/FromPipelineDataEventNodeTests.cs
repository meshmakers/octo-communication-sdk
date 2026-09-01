using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Triggers;

public class FromPipelineDataEventNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    [Fact]
    public async Task StartAsync_RegistersEventConsumer_WithCorrectExchangeAndRoutingKey()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        var endpointHandle = A.Fake<EndpointHandle>();

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
            A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._)).Returns(endpointHandle);

        var triggerContext = CreateTriggerContext();

        var testee = new FromPipelineDataEventNode(eventHubControl);

        await testee.StartAsync(triggerContext);

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
            A<string>.That.Contains("octo::com::dataflow-"),
            A<string>._,
            A<Func<PipelineDataReceived, Task>>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StartAsync_RegistersWithCorrectExchangeNameAndRoutingKey()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        var endpointHandle = A.Fake<EndpointHandle>();
        string? capturedExchangeName = null;
        string? capturedRoutingKey = null;

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
                A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._))
            .Invokes((string exchangeName, string routingKey, Func<PipelineDataReceived, Task> _) =>
            {
                capturedExchangeName = exchangeName;
                capturedRoutingKey = routingKey;
            })
            .Returns(endpointHandle);

        var dataFlowRtId = OctoObjectId.GenerateNewId();
        var pipelineRtEntityId = new RtEntityId(new RtCkId<CkTypeId>("System.Communication/DataFlow"), dataFlowRtId);
        var triggerContext = CreateTriggerContext("my-tenant", dataFlowRtId, pipelineRtEntityId);

        var testee = new FromPipelineDataEventNode(eventHubControl);

        await testee.StartAsync(triggerContext);

        Assert.NotNull(capturedExchangeName);
        Assert.Contains("my-tenant", capturedExchangeName);
        Assert.Contains(dataFlowRtId.ToString()!.ToLower(), capturedExchangeName);
        Assert.NotNull(capturedRoutingKey);
        Assert.Equal(pipelineRtEntityId.RtId.ToString(), capturedRoutingKey);
    }

    [Fact]
    public async Task StopAsync_AfterStart_CompletesWithoutError()
    {
        var eventHubControl = A.Fake<IEventHubControl>();

        var triggerContext = CreateTriggerContext();
        var testee = new FromPipelineDataEventNode(eventHubControl);
        await testee.StartAsync(triggerContext);

        await testee.StopAsync(triggerContext);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotThrow()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        var triggerContext = CreateTriggerContext();
        var testee = new FromPipelineDataEventNode(eventHubControl);

        await testee.StopAsync(triggerContext);
    }

    [Fact]
    public async Task StartAsync_RegistersBothEventAndCommandConsumers()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        var eventEndpointHandle = A.Fake<EndpointHandle>();
        var commandEndpointHandle = A.Fake<EndpointHandle>();

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
            A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._)).Returns(eventEndpointHandle);
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
            A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._)).Returns(commandEndpointHandle);

        var triggerContext = CreateTriggerContext();
        var testee = new FromPipelineDataEventNode(eventHubControl);

        await testee.StartAsync(triggerContext);

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
            A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
            A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StartAsync_RegistersCommandConsumer_WithCorrectAddress()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        string? capturedCommandAddress = null;

        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
                A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._))
            .Invokes((string addr, ExecuteCommandHandler<PipelineDataCommandRequest> _) =>
                capturedCommandAddress = addr)
            .Returns(A.Fake<EndpointHandle>());

        var dataFlowRtId = OctoObjectId.GenerateNewId();
        var pipelineRtId = OctoObjectId.GenerateNewId();
        var pipelineRtEntityId = new RtEntityId(
            new RtCkId<CkTypeId>("System.Communication/Pipeline"), pipelineRtId);
        var triggerContext = CreateTriggerContext("my-tenant", dataFlowRtId, pipelineRtEntityId);

        var testee = new FromPipelineDataEventNode(eventHubControl);

        await testee.StartAsync(triggerContext);

        Assert.NotNull(capturedCommandAddress);
        var expectedAddress =
            $"pipelinedatacommand-my-tenant-dataflow-{dataFlowRtId.ToString()!.ToLower()}-pipeline-{pipelineRtId.ToString()!.ToLower()}";
        Assert.Equal(expectedAddress, capturedCommandAddress);
    }

    [Fact]
    public async Task StopAsync_AfterStart_DisposesBothHandles()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        var eventEndpointHandle = A.Fake<EndpointHandle>();
        var commandEndpointHandle = A.Fake<EndpointHandle>();

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
            A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._)).Returns(eventEndpointHandle);
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
            A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._)).Returns(commandEndpointHandle);

        var triggerContext = CreateTriggerContext();
        var testee = new FromPipelineDataEventNode(eventHubControl);
        await testee.StartAsync(triggerContext);

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
            A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
            A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._)).MustHaveHappenedOnceExactly();
        await testee.StopAsync(triggerContext);
    }

    [Fact]
    public async Task CommandReceived_PipelineSucceeds_SendsSuccessResponse()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        ExecuteCommandHandler<PipelineDataCommandRequest>? capturedHandler = null;

        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
                A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._))
            .Invokes((string _, ExecuteCommandHandler<PipelineDataCommandRequest> handler) =>
                capturedHandler = handler)
            .Returns(A.Fake<EndpointHandle>());

        var triggerContext = CreateTriggerContext();
        var pipelineResult = new { processed = true, output = 42 };
        A.CallTo(() => triggerContext.ExecuteAsync(
                A<ExecutePipelineOptions>._, A<object?>._))
            .Returns(Task.FromResult<object?>(pipelineResult));

        var testee = new FromPipelineDataEventNode(eventHubControl);
        await testee.StartAsync(triggerContext);

        Assert.NotNull(capturedHandler);

        var request = new PipelineDataCommandRequest
        {
            TenantId = "test-tenant",
            Value = "{\"input\":\"data\"}",
            TransactionStartedDateTime = DateTime.UtcNow
        };

        object? capturedResponse = null;
        await capturedHandler(request, async response =>
        {
            capturedResponse = response;
            await Task.CompletedTask;
        });

        A.CallTo(() => triggerContext.ExecuteAsync(
            A<ExecutePipelineOptions>._, A<object?>._)).MustHaveHappenedOnceExactly();

        Assert.NotNull(capturedResponse);
        var typedResponse = Assert.IsType<PipelineDataCommandResponse>(capturedResponse);
        Assert.True(typedResponse.Success);
        Assert.NotNull(typedResponse.Result);
        var resultObj = JsonNode.Parse(typedResponse.Result)!;
        Assert.Equal(42, resultObj["output"]?.GetValue<int>());
    }

    [Fact]
    public async Task CommandReceived_PipelineFails_SendsErrorResponse()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        ExecuteCommandHandler<PipelineDataCommandRequest>? capturedHandler = null;

        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
                A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._))
            .Invokes((string _, ExecuteCommandHandler<PipelineDataCommandRequest> handler) =>
                capturedHandler = handler)
            .Returns(A.Fake<EndpointHandle>());

        var triggerContext = CreateTriggerContext();
        A.CallTo(() => triggerContext.ExecuteAsync(
                A<ExecutePipelineOptions>._, A<object?>._))
            .ThrowsAsync(new InvalidOperationException("Variable not found"));

        var testee = new FromPipelineDataEventNode(eventHubControl);
        await testee.StartAsync(triggerContext);

        Assert.NotNull(capturedHandler);

        var request = new PipelineDataCommandRequest
        {
            TenantId = "test-tenant",
            Value = "{\"input\":\"data\"}",
            TransactionStartedDateTime = DateTime.UtcNow
        };

        object? capturedResponse = null;
        await capturedHandler(request, async response =>
        {
            capturedResponse = response;
            await Task.CompletedTask;
        });

        Assert.NotNull(capturedResponse);
        var typedResponse = Assert.IsType<PipelineDataCommandResponse>(capturedResponse);
        Assert.False(typedResponse.Success);
        Assert.Contains("Variable not found", typedResponse.ErrorMessage);
    }

    /// <summary>
    ///     AB#5045: an execution started from a pipeline data event begins WITHOUT a caller identity.
    /// </summary>
    /// <remarks>
    ///     The caller identity of the pipeline that raised the event ends at
    ///     <c>ToPipelineDataEvent@1</c>; this execution resolves its own service identity (AB#5028).
    ///     Forwarding a principal or the caller's token across the bus would let whoever may enqueue
    ///     into the data flow act as whoever last triggered the sending pipeline, against a target that
    ///     never authenticated them — and on this fire-and-forget path the message has no bounded
    ///     lifetime, so the identity would stay usable for as long as it sits in the queue. This test
    ///     exists so a well-meant "the identity should survive the chain" change fails here.
    /// </remarks>
    [Fact]
    public async Task EventReceived_StartsTheExecutionWithoutAPrincipalAndWithoutACallerToken()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        Func<PipelineDataReceived, Task>? capturedHandler = null;

        A.CallTo(() => eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(
                A<string>._, A<string>._, A<Func<PipelineDataReceived, Task>>._))
            .Invokes((string _, string _, Func<PipelineDataReceived, Task> handler) => capturedHandler = handler)
            .Returns(A.Fake<EndpointHandle>());

        var triggerContext = CreateTriggerContext();
        ExecutePipelineOptions? capturedOptions = null;
        A.CallTo(() => triggerContext.ExecuteAsync(A<ExecutePipelineOptions>._, A<object?>._))
            .Invokes(call => capturedOptions = (ExecutePipelineOptions)call.Arguments[0]!)
            .Returns(Task.FromResult<object?>(null));

        var testee = new FromPipelineDataEventNode(eventHubControl);
        await testee.StartAsync(triggerContext);

        Assert.NotNull(capturedHandler);
        await capturedHandler!(new PipelineDataReceived
        {
            TenantId = "test-tenant",
            Value = "{\"input\":\"data\"}",
            TransactionStartedDateTime = DateTime.UtcNow
        });

        Assert.NotNull(capturedOptions);
        Assert.Null(capturedOptions!.VerifiedPrincipal);
        Assert.Null(capturedOptions.CallerAccessToken);
    }

    /// <summary>
    ///     The AwaitResult twin of
    ///     <see cref="EventReceived_StartsTheExecutionWithoutAPrincipalAndWithoutACallerToken" />.
    /// </summary>
    /// <remarks>
    ///     Covered separately because the command path is the one that <i>looks</i> like a synchronous
    ///     call — the sender blocks on the response, so it reads like an in-process hand-off where
    ///     carrying the caller along would seem harmless. It is the same bus hop with the same
    ///     enqueue-side authorization, and the target must resolve its own identity here too.
    /// </remarks>
    [Fact]
    public async Task CommandReceived_StartsTheExecutionWithoutAPrincipalAndWithoutACallerToken()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        ExecuteCommandHandler<PipelineDataCommandRequest>? capturedHandler = null;

        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
                A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._))
            .Invokes((string _, ExecuteCommandHandler<PipelineDataCommandRequest> handler) =>
                capturedHandler = handler)
            .Returns(A.Fake<EndpointHandle>());

        var triggerContext = CreateTriggerContext();
        ExecutePipelineOptions? capturedOptions = null;
        A.CallTo(() => triggerContext.ExecuteAsync(A<ExecutePipelineOptions>._, A<object?>._))
            .Invokes(call => capturedOptions = (ExecutePipelineOptions)call.Arguments[0]!)
            .Returns(Task.FromResult<object?>(null));

        var testee = new FromPipelineDataEventNode(eventHubControl);
        await testee.StartAsync(triggerContext);

        Assert.NotNull(capturedHandler);
        await capturedHandler!(new PipelineDataCommandRequest
        {
            TenantId = "test-tenant",
            Value = "{\"input\":\"data\"}",
            TransactionStartedDateTime = DateTime.UtcNow
        }, _ => Task.CompletedTask);

        Assert.NotNull(capturedOptions);
        Assert.Null(capturedOptions!.VerifiedPrincipal);
        Assert.Null(capturedOptions.CallerAccessToken);
    }

    private ITriggerContext CreateTriggerContext(string tenantId = "test-tenant",
        OctoObjectId? dataFlowRtId = null, RtEntityId? pipelineRtEntityId = null)
    {
        var rtId = dataFlowRtId ?? OctoObjectId.GenerateNewId();
        var pipelineId = pipelineRtEntityId ??
                         new RtEntityId(new RtCkId<CkTypeId>("System.Communication/Pipeline"), OctoObjectId.GenerateNewId());
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("FromPipelineDataEvent", 0,
            new FromPipelineDataEventNodeConfiguration(), dataContext);

        var triggerContext = A.Fake<ITriggerContext>();
        A.CallTo(() => triggerContext.NodeContext).Returns(nodeContext);
        A.CallTo(() => triggerContext.TenantId).Returns(tenantId);
        A.CallTo(() => triggerContext.DataFlowRtId).Returns(rtId);
        A.CallTo(() => triggerContext.PipelineRtEntityId).Returns(pipelineId);

        return triggerContext;
    }
}
