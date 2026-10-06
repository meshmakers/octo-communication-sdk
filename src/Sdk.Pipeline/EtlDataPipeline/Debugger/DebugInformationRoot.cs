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

    /// <summary>
    /// Gets the JSONPaths of the values masked as <c>***</c> per debug point (key: node id, see
    /// <see cref="DebugPointDto.NodeId" />); debug points without masked values are not listed
    /// (AB#5538, handover §11). Paths are rooted at the snapshot object: <c>$.input…</c>,
    /// <c>$.output…</c>, <c>$.dryRunIntent…</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RedactedPaths { get; set; } =
        new Dictionary<string, IReadOnlyList<string>>();
}