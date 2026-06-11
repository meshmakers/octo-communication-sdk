using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// A context for an ETL process
/// </summary>
public interface IEtlContext
{
    /// <summary>
    /// Gets the tenant id
    /// </summary>
    string TenantId { get; }
    
    /// <summary>
    /// Gets a guid that identifies the pipeline execution instance.
    /// </summary>
    Guid PipelineExecutionId { get; }
    
    /// <summary>
    /// Returns the data flow id.
    /// </summary>
    OctoObjectId DataFlowRtId { get; }
    
    /// <summary>
    /// Returns the pipeline id.
    /// </summary>
    RtEntityId PipelineRtEntityId { get; }
            
    /// <summary>
    /// Gets the transaction started date time.
    /// </summary>
    DateTime TransactionStartedDateTime { get; }
    
    /// <summary>
    /// Gets the date time when the value was received by an optional external system.
    /// </summary>
    DateTime? ExternalReceivedDateTime { get; }
    
    /// <summary>
    /// Gets properties that are shared between the different stages of the ETL process and different runs of the
    /// pipeline
    /// </summary>
    IDictionary<string, object?> Properties { get; } 
    
    /// <summary>
    /// Gets the global configuration for the pipeline
    /// </summary>
    IGlobalConfiguration GlobalConfiguration { get; }
}