using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Represents the default implementation of the <see cref="IEtlContext"/> interface.
/// </summary>
public class DefaultEtlContext : IEtlContext
{
    /// <summary>
    /// Creates a new instance of the <see cref="DefaultEtlContext"/> class.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="dataFlowRtId">Data flow runtime identifier</param>
    /// <param name="pipelineExecutionId">Guid that identifies the pipeline execution instance</param>
    /// <param name="pipelineRtEntityId">Pipeline identifier</param>
    /// <param name="transactionStartedDateTime">Date and time when the transaction started</param>
    /// <param name="externalReceivedDateTime">Date and time when the value was received by an optional external system</param>
    /// <param name="globalConfiguration">Global configuration for the pipeline provided by associating configuration to a pipeline</param>
    /// <param name="properties">properties that are shared between the different stages of the ETL process and different runs of the pipeline</param>
    public DefaultEtlContext(string tenantId, OctoObjectId dataFlowRtId, Guid pipelineExecutionId, RtEntityId pipelineRtEntityId, DateTime transactionStartedDateTime, DateTime? externalReceivedDateTime, IGlobalConfiguration globalConfiguration, IDictionary<string, object?> properties)
    {
        TenantId = tenantId;
        PipelineExecutionId = pipelineExecutionId;
        DataFlowRtId = dataFlowRtId;
        PipelineRtEntityId = pipelineRtEntityId;
        ExternalReceivedDateTime = externalReceivedDateTime;
        TransactionStartedDateTime = transactionStartedDateTime;
        GlobalConfiguration = globalConfiguration;
        Properties = properties;
    }

    /// <inheritdoc />
    public string TenantId { get; }
    
    /// <inheritdoc />
    public Guid PipelineExecutionId { get; }

    /// <inheritdoc />
    public OctoObjectId DataFlowRtId { get; }

    /// <inheritdoc />
    public DateTime TransactionStartedDateTime { get; }
    
    /// <inheritdoc />
    public RtEntityId PipelineRtEntityId { get; }

    /// <inheritdoc />
    public DateTime? ExternalReceivedDateTime { get; }
    
    /// <inheritdoc />
    public IDictionary<string, object?> Properties { get; }

    /// <inheritdoc />
    public IGlobalConfiguration GlobalConfiguration { get; }
}