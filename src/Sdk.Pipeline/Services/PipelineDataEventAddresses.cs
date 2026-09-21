using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The one place that names the queue a <c>FromPipelineDataEvent@1</c> trigger consumes and a
///     <c>ToPipelineDataEvent@1</c> node publishes to (AB#5231). A durable, named queue per target
///     pipeline: a message published while the target's workload is scaled to zero waits on the
///     broker instead of vanishing into an exchange with no bound queue, and every replica of the
///     target competes on the same queue instead of each getting a copy.
/// </summary>
public static class PipelineDataEventAddresses
{
    private const string Prefix = "octo::com::pipeline-data-event";

    /// <summary>Queue name (without the <c>queue:</c> scheme) for events addressed to <paramref name="pipelineRtId"/>.</summary>
    public static string QueueName(string tenantId, OctoObjectId pipelineRtId)
        => $"{Prefix}-{tenantId.ToLowerInvariant()}-{pipelineRtId.ToString().ToLowerInvariant()}";

    /// <summary>The send address of that queue.</summary>
    public static Uri QueueAddress(string tenantId, OctoObjectId pipelineRtId)
        => new($"queue:{QueueName(tenantId, pipelineRtId)}");
}
