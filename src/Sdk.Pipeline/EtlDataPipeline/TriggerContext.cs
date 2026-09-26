using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Implementation of the extract node context
/// </summary>
/// <param name="tenantId">TenantId of pipeline</param>
/// <param name="dataFlowRtId">Runtime id of the data flow</param>
/// <param name="pipelineRtEntityId">Pipeline runtime id</param>
/// <param name="nodeContext">Node context of the triggering extract node</param>
/// <param name="globalConfiguration">Global configuration</param>
public abstract class TriggerContext(
    string tenantId,
    OctoObjectId dataFlowRtId,
    RtEntityId pipelineRtEntityId,
    INodeContext nodeContext,
    IGlobalConfiguration globalConfiguration) : ITriggerContext
{
    private readonly string _tenantId = tenantId;
    private readonly RtEntityId _pipelineRtEntityId = pipelineRtEntityId;

    /// <inheritdoc />
    public INodeContext NodeContext { get; } = nodeContext;

    /// <inheritdoc />
    public IGlobalConfiguration GlobalConfiguration { get; } = globalConfiguration;

    /// <inheritdoc />
    public string TenantId { get; } = tenantId;

    /// <inheritdoc />
    public OctoObjectId DataFlowRtId { get; } = dataFlowRtId;

    /// <inheritdoc />
    public RtEntityId PipelineRtEntityId { get; } = pipelineRtEntityId;

    /// <inheritdoc />
    public async Task<object?> ExecuteAsync(ExecutePipelineOptions executePipelineOptions, object? input = null)
    {
        var pipelineExecutionId = await StartExecutePipelineAsync(executePipelineOptions, input);

        return await EndExecutePipelineAsync(pipelineExecutionId);
    }

    /// <inheritdoc />
    public abstract Task<Guid> StartExecutePipelineAsync(ExecutePipelineOptions executePipelineOptions,
        object? value = null);

    /// <inheritdoc />
    public abstract Task<object?> EndExecutePipelineAsync(Guid pipelineExecutionId);

    /// <summary>
    /// <inheritdoc cref="ITriggerContext.ReportStatusAsync" />
    /// </summary>
    /// <remarks>
    /// A no-op here, deliberately virtual rather than abstract: the base is subclassed outside
    /// this repository (the mesh adapter keeps a hand-maintained copy of the SDK's trigger
    /// context), and a status line nobody delivers is harmless, whereas an abstract member would
    /// break every such subclass on the package update. The hosts that own an
    /// <see cref="IPipelineExecutionReporter" /> override it.
    /// </remarks>
    public virtual Task ReportStatusAsync(string message, bool isError = false,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}