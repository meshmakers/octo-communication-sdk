using System.Collections.Concurrent;
using FakeItEasy;
using MassTransit;
using Meshmakers.Octo.Common.DistributionEventHub.Configuration.Options;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Triggers;

/// <summary>
///     AB#8105: the pipeline triggers declare their command consumers <b>exclusive by design</b> (option from AB#8363),
///     so registering a pipeline writes no AB#6909 deprecation warning and the topology is a stated decision.
/// </summary>
/// <remarks>
///     <para>
///         The first group checks what the nodes hand to <see cref="IEventHubControl" />: the options overload with
///         <see cref="CommandConsumerOptions.ExclusiveByDesignReason" /> set and nothing that would change the topology,
///         never the classic two-argument overload.
///     </para>
///     <para>
///         The second group runs the nodes against the real <see cref="IEventHubControl" /> of the event hub package
///         (resolved through <c>AddDistributionEventHub</c>, only the MassTransit bus is faked, no broker) and asserts on
///         its log: no deprecation warning, the reason logged at Debug, and the receive endpoint still named after the
///         classic address (no <c>::shared</c> queue). A control registration through the classic overload in the same
///         harness does log the warning, so the absence is not an artefact of the harness.
///     </para>
/// </remarks>
public class PipelineTriggerCommandConsumerTopologyTests
{
    private const string InstancePrefix = "ab8105";
    private const string DeprecationMarker = "Deprecated (AB#6909)";

    // ---- What the nodes pass to IEventHubControl --------------------------------------------------------------

    [Fact]
    public async Task FromExecutePipelineCommand_RegistersExclusiveByDesign_WithTheDeclaredReason()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        CommandConsumerOptions? captured = null;
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<ExecutePipelineRequest>(
                A<string>._, A<ExecuteCommandHandler<ExecutePipelineRequest>>._, A<CommandConsumerOptions>._))
            .Invokes((string _, ExecuteCommandHandler<ExecutePipelineRequest> _, CommandConsumerOptions o) =>
                captured = o)
            .Returns(A.Fake<EndpointHandle>());

        await new FromExecutePipelineCommandNode(eventHubControl).StartAsync(CreateTriggerContext());

        AssertExclusiveByDesign(captured, FromExecutePipelineCommandNode.ExclusiveByDesignReason);
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<ExecutePipelineRequest>(
                A<string>._, A<ExecuteCommandHandler<ExecutePipelineRequest>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task FromPipelineDataEvent_RegistersCommandConsumerExclusiveByDesign_WithTheDeclaredReason()
    {
        var eventHubControl = A.Fake<IEventHubControl>();
        CommandConsumerOptions? captured = null;
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
                A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._, A<CommandConsumerOptions>._))
            .Invokes((string _, ExecuteCommandHandler<PipelineDataCommandRequest> _, CommandConsumerOptions o) =>
                captured = o)
            .Returns(A.Fake<EndpointHandle>());

        await new FromPipelineDataEventNode(eventHubControl).StartAsync(CreateTriggerContext());

        AssertExclusiveByDesign(captured, FromPipelineDataEventNode.CommandExclusiveByDesignReason);
        A.CallTo(() => eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(
                A<string>._, A<ExecuteCommandHandler<PipelineDataCommandRequest>>._))
            .MustNotHaveHappened();
    }

    // ---- Against the real event hub control (fake bus, no broker) ---------------------------------------------

    [Fact]
    public async Task FromExecutePipelineCommand_RegisteringAPipeline_LogsNoDeprecationWarning_AndKeepsTheClassicQueue()
    {
        using var harness = new EventHubHarness();
        var context = CreateTriggerContext();
        var expectedQueue =
            $"{InstancePrefix}-{PipelineQueueNames.ExecutePipelineCommand.ToLower()}-{context.TenantId.ToLower()}" +
            $"-pipeline-{context.PipelineRtEntityId.RtId.ToString()!.ToLower()}";

        await new FromExecutePipelineCommandNode(harness.EventHubControl).StartAsync(context);

        Assert.Contains(expectedQueue, harness.ConnectedQueues);
        Assert.DoesNotContain(harness.ConnectedQueues, q => q.Contains("::shared", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs, l => l.Message.Contains(DeprecationMarker, StringComparison.Ordinal));
        Assert.Contains(harness.Logs, l => l.Level == LogLevel.Debug &&
                                           l.Message.Contains(expectedQueue, StringComparison.Ordinal) &&
                                           l.Message.Contains(FromExecutePipelineCommandNode.ExclusiveByDesignReason,
                                               StringComparison.Ordinal));
    }

    [Fact]
    public async Task FromPipelineDataEvent_RegisteringAPipeline_LogsNoDeprecationWarning_AndKeepsTheClassicQueue()
    {
        using var harness = new EventHubHarness();
        var context = CreateTriggerContext();
        var expectedCommandQueue =
            $"{InstancePrefix}-pipelinedatacommand-{context.TenantId.ToLower()}-dataflow-" +
            $"{context.DataFlowRtId.ToString()!.ToLower()}-pipeline-{context.PipelineRtEntityId.RtId.ToString()!.ToLower()}";

        await new FromPipelineDataEventNode(harness.EventHubControl).StartAsync(context);

        Assert.Contains(expectedCommandQueue, harness.ConnectedQueues);
        Assert.DoesNotContain(harness.ConnectedQueues, q => q.Contains("::shared", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs, l => l.Message.Contains(DeprecationMarker, StringComparison.Ordinal));
        Assert.Contains(harness.Logs, l => l.Level == LogLevel.Debug &&
                                           l.Message.Contains(expectedCommandQueue, StringComparison.Ordinal) &&
                                           l.Message.Contains(FromPipelineDataEventNode.CommandExclusiveByDesignReason,
                                               StringComparison.Ordinal));
    }

    [Fact]
    public void Control_AClassicRegistrationInTheSameHarness_LogsTheDeprecationWarning()
    {
        using var harness = new EventHubHarness();
        // Unique name: the event hub warns once per command name and process.
        var commandName = $"ab8105-control-{Guid.NewGuid():N}";

        harness.EventHubControl.RegisterCommandConsumer<ExecutePipelineRequest>(commandName, (_, _) => Task.CompletedTask);

        Assert.Contains(harness.Logs, l => l.Level == LogLevel.Warning &&
                                           l.Message.Contains(DeprecationMarker, StringComparison.Ordinal) &&
                                           l.Message.Contains(commandName, StringComparison.Ordinal));
    }

    private static void AssertExclusiveByDesign(CommandConsumerOptions? options, string expectedReason)
    {
        Assert.NotNull(options);
        Assert.False(string.IsNullOrWhiteSpace(expectedReason));
        Assert.Equal(expectedReason, options!.ExclusiveByDesignReason);
        Assert.Contains("AB#8105", expectedReason, StringComparison.Ordinal);
        // Nothing that would change the topology: no migration, no bridge probing, no consume topology.
        Assert.False(options.MigratesLegacyCommand);
        Assert.Null(options.LegacyProbeInterval);
        Assert.False(options.ConfigureConsumeTopology);
    }

    private static ITriggerContext CreateTriggerContext()
    {
        var context = A.Fake<ITriggerContext>();
        A.CallTo(() => context.TenantId).Returns("Tenant-AB8105");
        A.CallTo(() => context.DataFlowRtId).Returns(OctoObjectId.GenerateNewId());
        A.CallTo(() => context.PipelineRtEntityId).Returns(
            new RtEntityId(new RtCkId<CkTypeId>("System.Communication/Pipeline"), OctoObjectId.GenerateNewId()));
        return context;
    }

    /// <summary>
    ///     The event hub's own <see cref="IEventHubControl" /> with a fake MassTransit bus (records the receive endpoint
    ///     names) and a log sink. No broker connection is made.
    /// </summary>
    private sealed class EventHubHarness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly CapturingLoggerProvider _logs = new();

        public EventHubHarness()
        {
            var bus = A.Fake<IBusControl>();
            A.CallTo(() => bus.ConnectReceiveEndpoint(A<string>._, A<Action<IReceiveEndpointConfigurator>>._))
                .Invokes((string queueName, Action<IReceiveEndpointConfigurator>? _) => ConnectedQueues.Add(queueName))
                .Returns(A.Fake<HostReceiveEndpointHandle>());

            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(_logs));
            services.Configure<DistributionEventHubOptions>(o => o.InstancePrefix = InstancePrefix);
            services.AddDistributionEventHub(config =>
            {
                config.UniqueServiceAddress = "ab8105-tests";
                config.AutomaticallyStartBusDuringStartup = false;
            });
            // Replace the MassTransit bus: the control only connects receive endpoints on it.
            services.AddSingleton(bus);
            _provider = services.BuildServiceProvider();
            EventHubControl = _provider.GetRequiredService<IEventHubControl>();
        }

        public IEventHubControl EventHubControl { get; }

        public ConcurrentBag<string> ConnectedQueues { get; } = [];

        public IReadOnlyCollection<(LogLevel Level, string Message)> Logs => _logs.Entries.ToArray();

        public void Dispose() => _provider.Dispose();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
