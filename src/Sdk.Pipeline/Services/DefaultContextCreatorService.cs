using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Default implementation of the <see cref="IContextCreatorService"/> interface
/// </summary>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class DefaultContextCreatorService(IServiceProvider serviceProvider) : IContextCreatorService
{
    /// <inheritdoc />
    public virtual ITriggerContext CreateTriggerContext(string tenantId, OctoObjectId dataFlowRtId, RtEntityId pipelineRtEntityId,
        INodeContext nodeContext, IGlobalConfiguration globalConfiguration)
    {
        return new AdapterTriggerContext(serviceProvider, tenantId, dataFlowRtId, pipelineRtEntityId, nodeContext,
            globalConfiguration);
    }

    /// <inheritdoc />
    public virtual Task<TContext> CreateEtlContext<TContext>(PipelineRegistration pipelineRegistration,
        ExecutePipelineOptions executePipelineOptions, Guid pipelineExecutionId) where TContext : class, IEtlContext
    {
        var context = new DefaultEtlContext(pipelineRegistration.TenantId,
            pipelineRegistration.DataFlowRtId,
            pipelineExecutionId,
            pipelineRegistration.PipelineRtEntityId, executePipelineOptions.TransactionStartedDateTime,
            executePipelineOptions.ExternalReceivedDateTime, pipelineRegistration.GlobalConfiguration,
            pipelineRegistration.Dictionary);

        var etlContext = context as TContext;
        return Task.FromResult(etlContext ?? throw PipelineExecutionException.EtlContextTypeMismatch<TContext>(context));
    }
}