using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;

/// <summary>
/// Configuration for the distribution event hub node
/// </summary>
[NodeName("ToPipelineDataEvent", 1)]
public record ToPipelineDataEventNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the RtId of the target pipeline to route the data to.
    /// Must be a pipeline within the same DataFlow.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public OctoObjectId TargetPipelineRtId { get; set; }

    /// <summary>
    /// When true, sends a command and waits for the target pipeline to complete
    /// and return its result. When false (default), uses fire-and-forget pub/sub.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public bool AwaitResult { get; set; }

    /// <summary>
    /// Optional timeout in seconds for the await-result call.
    /// Only used when AwaitResult is true.
    /// </summary>
    [PropertyGroup("Timing", 0)]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// JSONPath where the target pipeline's result is placed in the data context.
    /// Only used when AwaitResult is true.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string ResultTargetPath { get; set; } = "$.pipelineResult";
}

/// <summary>
/// Publishes the target object to the distribution event hub
/// </summary>
[NodeConfiguration(typeof(ToPipelineDataEventNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class ToPipelineDataEventNode(
    NodeDelegate next,
    IEtlContext adapterEtlContext,
    IDistributionEventHubService distributionEventHubService) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ToPipelineDataEventNodeConfiguration>();

        if (c.TargetPipelineRtId == OctoObjectId.Empty)
        {
            throw DataPipelineException.MissingRequiredConfiguration("ToPipelineDataEvent", "TargetPipelineRtId");
        }

        // Transform the data context so that only the target object is sent
        var o = dataContext.Get<JsonNode>(c.Path);
        var target = new JsonObject();
        if (o != null)
        {
            JsonNodePath.Set(target, c.TargetPath, o);
        }

        var serializedValue = JsonSerializer.Serialize(target, SystemTextJsonOptions.Default);

        if (c.AwaitResult)
        {
            var commandAddress =
                $"pipelinedatacommand-{adapterEtlContext.TenantId.ToLower()}-dataflow-{adapterEtlContext.DataFlowRtId.ToString()?.ToLower()}-pipeline-{c.TargetPipelineRtId.ToString()?.ToLower()}";

            var request = new PipelineDataCommandRequest
            {
                TenantId = adapterEtlContext.TenantId,
                DataFlowRtId = adapterEtlContext.DataFlowRtId,
                PipelineRtEntityId = adapterEtlContext.PipelineRtEntityId,
                Value = serializedValue,
                TransactionStartedDateTime = adapterEtlContext.TransactionStartedDateTime,
                ExternalReceivedDateTime = adapterEtlContext.ExternalReceivedDateTime
            };

            var timeout = c.TimeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(c.TimeoutSeconds.Value)
                : (TimeSpan?)null;

            var response = await distributionEventHubService.GetCommandResponseAsync<PipelineDataCommandRequest, PipelineDataCommandResponse>(
                commandAddress, request, default, timeout);

            if (!response.Success)
            {
                throw DataPipelineException.TargetPipelineFailed(response.ErrorMessage);
            }

            if (response.Result != null)
            {
                var resultNode = JsonNode.Parse(response.Result);
                dataContext.Set<JsonNode>(c.ResultTargetPath, resultNode, DocumentModes.Extend,
                    ValueKinds.Simple, TargetValueWriteModes.Overwrite);
            }
        }
        else
        {
            // Fire-and-forget: existing pub/sub behavior
            // if we don't define a timeout here, we will wait until the message is sent which can take quite a long time
            // when we don't have a connection to the event hub.
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var message = new PipelineDataReceived
            {
                TenantId = adapterEtlContext.TenantId,
                DataFlowRtId = adapterEtlContext.DataFlowRtId,
                PipelineRtEntityId = adapterEtlContext.PipelineRtEntityId,
                Value = serializedValue,
                TransactionStartedDateTime = adapterEtlContext.TransactionStartedDateTime,
                ExternalReceivedDateTime = adapterEtlContext.ExternalReceivedDateTime
            };

            var exchangeName =
                $"octo::com::dataflow-{adapterEtlContext.TenantId.ToLower()}-{adapterEtlContext.DataFlowRtId.ToString()?.ToLower()}";

            await distributionEventHubService.SendToExchangeAsync(exchangeName,
                c.TargetPipelineRtId.ToString(), message, cts.Token);
        }

        await next(dataContext, nodeContext);
    }
}
