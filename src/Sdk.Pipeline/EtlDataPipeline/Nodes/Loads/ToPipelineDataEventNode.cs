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

        RecordIdentityBoundary(adapterEtlContext, nodeContext, c);

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

    /// <summary>
    ///     Records that an execution's caller identity <b>ends here</b> (AB#5045).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A pipeline data event crosses the message bus, and the trigger on the other side
    ///         (<c>FromPipelineDataEvent@1</c>) builds its <c>ExecutePipelineOptions</c> without a
    ///         <c>VerifiedPrincipal</c> and without a caller token — deliberately, and permanently.
    ///         The target execution therefore resolves its own identity (<c>PipelineIdentityResolver</c>,
    ///         AB#5028): the pipeline's service account, or the system context on a tenant that has none.
    ///     </para>
    ///     <para>
    ///         🔴 <b>Why the identity is not forwarded.</b> Doing so would let a pipeline act as a caller
    ///         it never authenticated against: the sender picks the routing key, so whoever may enqueue
    ///         into the data flow would inherit the identity of whoever last triggered the sending
    ///         pipeline — and on the fire-and-forget path the message has no bounded lifetime, so the
    ///         identity would stay usable for as long as it sits in the queue. That is a privilege
    ///         escalation, and it is not something to introduce as a side effect of a chaining node.
    ///     </para>
    ///     <para>
    ///         What the decision costs is that one logical request runs half under the user and half
    ///         under the service — so the hand-off is made <b>visible</b> instead of silent, on the
    ///         execution log the adapter and the Studio debug panel already surface. Deliberately not on
    ///         the message: its payload is pipeline data, and no credential may ever travel on it.
    ///     </para>
    /// </remarks>
    private static void RecordIdentityBoundary(IEtlContext etlContext, INodeContext nodeContext,
        ToPipelineDataEventNodeConfiguration configuration)
    {
        var subjectId = etlContext.VerifiedPrincipal?.SubjectId;
        var hasCallerToken = !string.IsNullOrEmpty(etlContext.CallerAccessToken);

        if (subjectId == null && !hasCallerToken)
        {
            // Nothing ends here: this execution has no caller identity either, so both sides run on
            // a service identity and the chain is homogeneous.
            nodeContext.Debug(
                "Pipeline data event to '{HomogeneousTargetPipelineRtId}': this execution carries no caller identity, so the target execution is service-identity based like this one (AB#5045).",
                configuration.TargetPipelineRtId);
            return;
        }

        nodeContext.Info(
            "The caller identity of this execution ({BoundarySubjectId}{BoundaryTokenState}) ENDS HERE (AB#5045): "
            + "the pipeline data event to '{BoundaryTargetPipelineRtId}' carries neither the principal nor the caller's "
            + "token, so the target execution runs under its own service identity (AB#5028) and sees what that account "
            + "may see - not what the caller may see. Not forwarding it is deliberate: a pipeline must not be able to "
            + "act as a caller the target never authenticated.",
            subjectId ?? "no principal, caller token only", hasCallerToken ? ", with a caller token" : string.Empty,
            configuration.TargetPipelineRtId);
    }
}
