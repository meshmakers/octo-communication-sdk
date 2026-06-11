namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

internal class NodeLookup(string qualifiedName, Type nodeType, Type nodeConfigurationType)
{
    /// <summary>
    /// Gets the qualified name of the node.
    /// </summary>
    public string QualifiedName { get; } = qualifiedName;
    
    /// <summary>
    /// Gets the type of the node.
    /// </summary>
    public Type NodeType { get; } = nodeType;
    
    /// <summary>
    /// Gets the type of the node configuration.
    /// </summary>
    public Type NodeConfigurationType { get; } = nodeConfigurationType;
}