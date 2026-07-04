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
        // The endpoint is keyed by the PIPELINE rtId, not the DataFlow rtId. Keying by DataFlow made
        // two FromExecutePipelineCommand pipelines in the same DataFlow register the same receive
        // endpoint ("A receive endpoint with the same key was already added"), so only the first
        // deployed. ExecutePipeline always targets a specific pipeline, so the pipeline id is the
        // correct scope. Must stay in sync with TriggerManagementService.StartExecutePipelineAsync.
        var address =
            $"{PipelineQueueNames.ExecutePipelineCommand.ToLower()}-{context.TenantId.ToLower()}-pipeline-{context.PipelineRtEntityId.RtId.ToString()?.ToLower()}";

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
                    var executeOptions = new ExecutePipelineOptions(startDateTime)
                    {
                        IsDryRun = message.IsDryRun
                    };
                    var pipelineExecutionId = await context.StartExecutePipelineAsync(executeOptions, input);
                    await responseFunc(new ExecutePipelineResponse(true, null, pipelineExecutionId, startDateTime));

                    // AB#4279: Do not hold the RabbitMQ delivery ack open for the whole pipeline run.
                    // A run exceeding the broker consumer_timeout (default 30 min) would tear down the
                    // channel and drop this auto-delete queue, silently losing the execution. The pipeline
                    // already runs as a detached background task and its final state (incl. Failed) is
                    // reported out-of-band via IPipelineExecutionReporter, so completion is awaited here
                    // decoupled from the bus handler — which now acks as soon as execution has started.
                    _ = CompleteExecutionDetachedAsync(context, pipelineExecutionId, message.TenantId);
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

    /// <summary>
    /// Awaits the running pipeline execution and reports its end state, decoupled from the bus
    /// consumer so a long-running pipeline (AB#4279) does not hold the RabbitMQ delivery ack open
    /// past the broker consumer_timeout. Exceptions are already surfaced as a Failed execution by
    /// <see cref="ITriggerContext.EndExecutePipelineAsync" />; they are logged and swallowed here so
    /// they never fault the (already-acked) consumer.
    /// </summary>
    private static async Task CompleteExecutionDetachedAsync(ITriggerContext context, Guid pipelineExecutionId,
        string tenantId)
    {
        try
        {
            await context.EndExecutePipelineAsync(pipelineExecutionId);
        }
        catch (Exception ex)
        {
            context.NodeContext.Error(ex,
                "[{TenantId}] Pipeline execution '{PipelineExecutionId}' failed after detached completion",
                tenantId, pipelineExecutionId);
        }
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
