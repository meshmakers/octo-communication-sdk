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
    private EndpointHandle? _endpointHandle;
    private EndpointHandle? _commandEndpointHandle;

    public Task StartAsync(ITriggerContext context)
    {
        var exchangeName =
            $"octo::com::dataflow-{context.TenantId.ToLower()}-{context.DataFlowRtId.ToString()?.ToLower()}";
        var routingKey = context.PipelineRtEntityId.RtId.ToString();

        // Pub/sub consumer (existing fire-and-forget behavior)
        _endpointHandle = eventHubControl.RegisterRoutedEventConsumer<PipelineDataReceived>(exchangeName, routingKey,
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

        // Command consumer (new: for AwaitResult callers)
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
                });

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
