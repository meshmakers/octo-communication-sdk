using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Service that creates a context for a pipeline
/// </summary>
public interface IContextCreatorService
{
    /// <summary>
    /// Creates a context for a trigger node
    /// </summary>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="dataFlowRtId">Runtime id of the data flow</param>
    /// <param name="pipelineRtEntityId">Runtime entity id of the pipeline</param>
    /// <param name="nodeContext">Node context of the triggering extract node</param>
    /// <param name="globalConfiguration">Global configuration</param>
    /// <returns>Create trigger context</returns>
    ITriggerContext CreateTriggerContext(string tenantId, OctoObjectId dataFlowRtId, RtEntityId pipelineRtEntityId,
        INodeContext nodeContext, IGlobalConfiguration globalConfiguration);

    /// <summary>
    /// Creates a data context
    /// </summary>
    /// <returns></returns>
    Task<TContext> CreateEtlContext<TContext>(PipelineRegistration pipelineRegistration,
        ExecutePipelineOptions executePipelineOptions, Guid pipelineExecutionId) where TContext : class, IEtlContext;
}