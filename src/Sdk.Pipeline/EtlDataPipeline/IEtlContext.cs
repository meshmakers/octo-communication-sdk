using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;

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

    /// <summary>
    /// Gets the authenticated caller of the trigger, verified by the trigger's own authorization
    /// (AB#4975). Null for anonymous and internal triggers. Deliberately NOT part of
    /// <see cref="Properties" /> — that dictionary is shared across pipeline runs.
    /// </summary>
    VerifiedPrincipal? VerifiedPrincipal => null;

    /// <summary>
    /// Gets the <b>raw access token</b> the caller presented to the trigger, for nodes that must act
    /// as the caller against another service (delegation / "on-behalf-of" — AB#5026 / AB#5031).
    /// Null for anonymous and internal triggers.
    /// </summary>
    /// <remarks>
    /// A default interface member so adapters implementing <see cref="IEtlContext" /> themselves are
    /// not broken by the addition — the same pattern <see cref="VerifiedPrincipal" /> uses.
    /// Deliberately NOT part of <see cref="Properties" /> (shared across pipeline runs) and NOT part
    /// of <see cref="VerifiedPrincipal" /> (projected into the persistable, echoed data root); see
    /// <see cref="Services.ExecutePipelineOptions.CallerAccessToken" /> for the full reasoning.
    /// Never log this value and never write it into the data context.
    /// </remarks>
    string? CallerAccessToken => null;
}