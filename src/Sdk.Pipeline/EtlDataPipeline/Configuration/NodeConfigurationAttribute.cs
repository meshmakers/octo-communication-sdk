namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Describes a node in the pipeline
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NodeConfigurationAttribute : Attribute
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="nodeConfigurationType">Type of the node configuration</param>
    public NodeConfigurationAttribute(Type nodeConfigurationType)
    {
        NodeConfigurationType = nodeConfigurationType;
    }

    /// <summary>
    /// Type of node configuration
    /// </summary>
    public Type NodeConfigurationType { get; set; }
}