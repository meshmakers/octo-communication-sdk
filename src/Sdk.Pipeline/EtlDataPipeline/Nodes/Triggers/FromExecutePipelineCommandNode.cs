#if NET10_0_OR_GREATER
using System.Text.Json.Nodes;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;

/// <summary>
/// Configuration for node FromExecutePipelineCommand
/// </summary>
[NodeName("FromExecutePipelineCommand", 1)]
public record FromExecutePipelineCommandNodeConfiguration : TriggerNodeConfiguration;

/// <summary>
/// Trigger node that listens for pipeline execution commands via the distribution event hub.
/// This enables manual pipeline execution from the Studio or API.
/// </summary>
[NodeConfiguration(typeof(FromExecutePipelineCommandNodeConfiguration))]
public class FromExecutePipelineCommandNode(IEventHubControl eventHubControl)
    : ITriggerPipelineNode
{
    private EndpointHandle? _endpointHandle;

    /// <inheritdoc />
    public Task StartAsync(ITriggerContext context)
    {
        var address =
            $"{PipelineQueueNames.ExecutePipelineCommand.ToLower()}-{context.TenantId.ToLower()}-data-flow-{context.DataFlowRtId.ToString()?.ToLower()}";

        _endpointHandle = eventHubControl.RegisterCommandConsumer<ExecutePipelineRequest>(address,
            async (message, responseFunc) =>
            {
                try
                {
                    context.NodeContext.Info("Received command executing pipeline");

                    JsonNode input = new JsonObject();
                    if (!string.IsNullOrWhiteSpace(message.PipelineInput))
                    {
                        input = JsonNode.Parse(message.PipelineInput) ?? new JsonObject();
                    }

                    var startDateTime = DateTime.UtcNow;
                    var pipelineExecutionId = await context.StartExecutePipelineAsync(new ExecutePipelineOptions(startDateTime), input);
                    await responseFunc(new ExecutePipelineResponse(true, null, pipelineExecutionId, startDateTime));

                    // Wait for pipeline completion and report execution end to communication controller
                    await context.EndExecutePipelineAsync(pipelineExecutionId);
                }
                catch (Exception ex)
                {
                    await responseFunc(new ExecutePipelineResponse(false, ex.Message, null, null));

                    context.NodeContext.Error(ex, "[{TenantId}] Error processing pipeline: '{PipelineId}'",
                        message.TenantId, context.PipelineRtEntityId);
                    throw;
                }
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(ITriggerContext context)
    {
        if (_endpointHandle != null)
        {
            await _endpointHandle.DisposeAsync();
        }
    }
}
#endif
