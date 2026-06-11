namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// The pipeline configuration root.
/// </summary>
public class NodeDefinitionRoot
{
    /// <summary>
    /// Trigger extract nodes of the current pipeline
    /// </summary>
    public ICollection<TriggerNodeConfiguration>? Triggers { get; set; }
    
    /// <summary>
    /// Transformations of the current node
    /// </summary>
    public ICollection<NodeConfiguration>? Transformations { get; set; }
}
