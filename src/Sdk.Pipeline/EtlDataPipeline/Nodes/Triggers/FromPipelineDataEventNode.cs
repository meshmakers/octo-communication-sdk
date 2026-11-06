using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;

/// <summary>
/// Configuration for node FromPipelineDataEvent
/// </summary>
[NodeName("FromPipelineDataEvent", 1)]
public record FromPipelineDataEventNodeConfiguration : TriggerNodeConfiguration;

// AB#5045 — an execution started from a pipeline data event has NO caller identity, on purpose.
//
// Both consumers below build their ExecutePipelineOptions without a VerifiedPrincipal and without a
// CallerAccessToken. The caller identity of the pipeline that raised the event ends at
// ToPipelineDataEvent@1, which records the hand-off on its execution log. This execution therefore
// resolves its own identity through the adapter's PipelineIdentityResolver (AB#5028): the pipeline's
// service account, or the system context on a tenant that has none.
//
// 🔴 Do not "fix" this by carrying the principal or the token across. The sender picks the routing
// key, so forwarding would let whoever may enqueue into the data flow act as whoever last triggered
// the sending pipeline — against a target that never authenticated them. On the pub/sub path the
// message has no bounded lifetime either, so the identity would stay usable for as long as it sits
// in the queue. That is a privilege escalation, not a convenience. FromPipelineDataEventNodeTests
// pins the absence of both values so a well-meant change fails a test rather than shipping.


[NodeConfiguration(typeof(FromPipelineDataEventNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
internal class FromPipelineDataEventNode(IEventHubControl eventHubControl)
    : ITriggerPipelineNode
{
    /// <summary>
    /// Why the pipeline-data command consumer (AwaitResult callers) stays exclusive (AB#8105, option from AB#8363).
    /// Logged by the event hub at registration (Debug) and listed in the octo-distributedEventHub CLAUDE.md audit table.
    /// </summary>
    internal const string CommandExclusiveByDesignReason =
        "addressed to one pipeline instance (pipelinedatacommand-{tenant}-dataflow-{id}-pipeline-{id}); the sender " +
        "(ToPipelineDataEvent with AwaitResult) uses the classic topology, so a further adapter replica is a hot " +
        "standby; going shared needs one legacy bridge per pipeline and belongs with the sender migration (AB#8105)";

    private EndpointHandle? _endpointHandle;
    private EndpointHandle? _commandEndpointHandle;

    public Task StartAsync(ITriggerContext context)
    {
        // AB#5231: a durable, named queue per target pipeline, consumed through the same overload
        // the cron trigger uses (Durable=true, AutoDelete=false). The former exchange binding gave
        // every process a private auto-delete queue with a Guid suffix — which is gone the moment
        // the workload is scaled to zero, so an event published then reached an exchange with no
        // bound queue and was discarded without an error. Now it waits on the broker; the sender
        // wakes the target (IPipelineDataEventTargetWaker) so somebody comes to consume it.
        var queueName = PipelineDataEventAddresses.QueueName(context.TenantId, context.PipelineRtEntityId.RtId);

        _endpointHandle = eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(queueName,
            async message =>
            {
                if (message.Value == null)
                {
                    context.NodeContext.Warning("Received message with null value");
                    return;
                }

                try
                {
                    var input = JsonNode.Parse(message.Value);
                    await context.ExecuteAsync(new ExecutePipelineOptions(message.TransactionStartedDateTime)
                        { ExternalReceivedDateTime = message.ExternalReceivedDateTime }, input);
                }
                catch (Exception ex)
                {
                    context.NodeContext.Error(ex, "Pipeline execution failed for pipeline data event");
                }
            });

        // Command consumer (for AwaitResult callers). Exclusive by design (AB#8105): exactly one process owns this
        // pipeline's command address. The address is per pipeline, ToPipelineDataEvent sends with the classic
        // exclusive topology, and a shared registration would need MigratesLegacyCommand — one legacy bridge per
        // pipeline probing the broker every 2 s, for every pipeline of every adapter. Same queue and wire behaviour
        // as before; the declaration only replaces the AB#6909 deprecation warning with a stated decision. The
        // fire-and-forget path above is a durable shared queue already (AB#5231) and is not affected.
        var commandAddress =
            $"pipelinedatacommand-{context.TenantId.ToLower()}-dataflow-{context.DataFlowRtId.ToString()?.ToLower()}-pipeline-{context.PipelineRtEntityId.RtId.ToString()?.ToLower()}";

        _commandEndpointHandle =
            eventHubControl.RegisterCommandConsumer<PipelineDataCommandRequest>(commandAddress,
                async (message, respondToCommand) =>
                {
                    try
                    {
                        JsonNode input = new JsonObject();
                        if (!string.IsNullOrWhiteSpace(message.Value))
                        {
                            input = JsonNode.Parse(message.Value!) ?? new JsonObject();
                        }

                        var result = await context.ExecuteAsync(
                            new ExecutePipelineOptions(message.TransactionStartedDateTime)
                                { ExternalReceivedDateTime = message.ExternalReceivedDateTime }, input);

                        var serializedResult = result != null
                            ? JsonSerializer.Serialize(result, SystemTextJsonOptions.Default)
                            : null;

                        await respondToCommand(new PipelineDataCommandResponse
                        {
                            Success = true, Result = serializedResult
                        });
                    }
                    catch (Exception ex)
                    {
                        await respondToCommand(new PipelineDataCommandResponse
                        {
                            Success = false, ErrorMessage = ex.Message
                        });
                    }
                },
                CommandConsumerOptions.ExclusiveByDesign(CommandExclusiveByDesignReason));

        return Task.CompletedTask;
    }

    public async Task StopAsync(ITriggerContext context)
    {
        if (_endpointHandle != null)
        {
            await _endpointHandle.DisposeAsync();
        }

        if (_commandEndpointHandle != null)
        {
            await _commandEndpointHandle.DisposeAsync();
        }
    }
}
