using FakeItEasy;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// Regression tests for AB#4279: the execute-pipeline bus consumer must acknowledge the RabbitMQ
/// delivery as soon as the execution has started, and must NOT hold the ack open for the whole
/// pipeline run (which would exceed the broker consumer_timeout and silently drop the auto-delete
/// queue for any run longer than ~30 min).
/// </summary>
public class FromExecutePipelineCommandNodeTests
{
    private const string TenantId = "test-tenant";

    private readonly IEventHubControl _eventHubControl;
    private readonly ITriggerContext _context;
    private readonly FromExecutePipelineCommandNode _sut;

    private ExecuteCommandHandler<ExecutePipelineRequest>? _capturedHandler;

    public FromExecutePipelineCommandNodeTests()
    {
        _eventHubControl = A.Fake<IEventHubControl>();
        _context = A.Fake<ITriggerContext>();

        A.CallTo(() => _context.TenantId).Returns(TenantId);
        A.CallTo(() => _context.NodeContext).Returns(A.Fake<INodeContext>());

        A.CallTo(() => _eventHubControl.RegisterCommandConsumer<ExecutePipelineRequest>(
                A<string>._, A<ExecuteCommandHandler<ExecutePipelineRequest>>._))
            .Invokes((string _, ExecuteCommandHandler<ExecutePipelineRequest> handler) =>
                _capturedHandler = handler);

        _sut = new FromExecutePipelineCommandNode(_eventHubControl);
    }

    [Fact]
    public async Task Handler_ReturnsAfterExecutionStarts_WithoutWaitingForCompletion()
    {
        // Arrange: a long-running pipeline whose completion never finishes within the test.
        var executionId = Guid.NewGuid();
        var neverCompletes = new TaskCompletionSource<object?>();

        A.CallTo(() => _context.StartExecutePipelineAsync(A<ExecutePipelineOptions>._, A<object?>._))
            .Returns(Task.FromResult(executionId));
        A.CallTo(() => _context.EndExecutePipelineAsync(executionId))
            .Returns(neverCompletes.Task);

        ExecutePipelineResponse? response = null;

        await _sut.StartAsync(_context);
        Assert.NotNull(_capturedHandler);

        // Act: run the bus consumer handler. It must complete promptly even though the pipeline
        // (EndExecutePipelineAsync) is still running — proving the ack is decoupled from the work.
        var handlerTask = _capturedHandler!(
            new ExecutePipelineRequest(TenantId, null),
            r =>
            {
                response = (ExecutePipelineResponse)r;
                return Task.CompletedTask;
            });

        await handlerTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert: the caller got the "started" response...
        Assert.NotNull(response);
        Assert.True(response!.IsSuccessStartingExecution);
        Assert.Equal(executionId, response.PipelineExecutionId);

        // ...and completion was kicked off detached from the (now-acked) consumer handler.
        A.CallTo(() => _context.EndExecutePipelineAsync(executionId)).MustHaveHappenedOnceExactly();

        neverCompletes.SetResult(null);
    }

    [Fact]
    public async Task Handler_WhenStartFails_RespondsWithFailureAndRethrows()
    {
        // Arrange: starting the execution fails before any response is sent.
        A.CallTo(() => _context.StartExecutePipelineAsync(A<ExecutePipelineOptions>._, A<object?>._))
            .ThrowsAsync(new InvalidOperationException("start failed"));

        ExecutePipelineResponse? response = null;

        await _sut.StartAsync(_context);
        Assert.NotNull(_capturedHandler);

        // Act & Assert: the failure is surfaced to the bus (rethrown) and reported to the caller.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _capturedHandler!(
            new ExecutePipelineRequest(TenantId, null),
            r =>
            {
                response = (ExecutePipelineResponse)r;
                return Task.CompletedTask;
            }));

        Assert.NotNull(response);
        Assert.False(response!.IsSuccessStartingExecution);
        Assert.Equal("start failed", response.ErrorMessage);

        // A start failure must never kick off detached completion.
        A.CallTo(() => _context.EndExecutePipelineAsync(A<Guid>._)).MustNotHaveHappened();
    }

    /// <summary>
    ///     AB#5029/AB#5045: an <c>ExecutePipeline</c> command starts the execution WITHOUT a caller
    ///     identity, so the pipeline resolves its own service identity (AB#5028).
    /// </summary>
    /// <remarks>
    ///     This is the trigger behind "Execute" in the Studio and behind the ExecutePipeline API, so it
    ///     is the row of the identity matrix most likely to be "improved" later — the person clicking
    ///     the button IS authenticated, so forwarding their principal looks obviously right. It is not:
    ///     the command travels over the bus and this node authenticates nobody, so a forwarded identity
    ///     would be an assertion the target cannot check. If the execution should ever run as the
    ///     clicking user, the identity has to be established here — verified, not relayed — and this
    ///     test is the place that change has to argue with.
    /// </remarks>
    [Fact]
    public async Task Handler_StartsTheExecutionWithoutAPrincipalAndWithoutACallerToken()
    {
        ExecutePipelineOptions? capturedOptions = null;
        A.CallTo(() => _context.StartExecutePipelineAsync(A<ExecutePipelineOptions>._, A<object?>._))
            .Invokes(call => capturedOptions = (ExecutePipelineOptions)call.Arguments[0]!)
            .Returns(Task.FromResult(Guid.NewGuid()));

        await _sut.StartAsync(_context);
        Assert.NotNull(_capturedHandler);

        await _capturedHandler!(new ExecutePipelineRequest(TenantId, null), _ => Task.CompletedTask);

        Assert.NotNull(capturedOptions);
        Assert.Null(capturedOptions!.VerifiedPrincipal);
        Assert.Null(capturedOptions.CallerAccessToken);
    }
}
