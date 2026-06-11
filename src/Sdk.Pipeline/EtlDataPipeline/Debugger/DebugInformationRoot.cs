using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// The serialized debug information
/// </summary>
public class DebugInformationRoot
{
    /// <summary>
    /// Gets the pipeline execution id, which is a guid that identifies the pipeline execution instance
    /// </summary>
    public Guid PipelineExecutionId { get; set; }
    
    /// <summary>
    /// Gets the pipeline runtime entity id
    /// </summary>
    public RtEntityId PipelineRtEntityId { get; set; }
    
    /// <summary>
    /// Gets the debug points
    /// </summary>
    public ICollection<DebugPointDto> DebugPoints { get; set; }= null!;
}