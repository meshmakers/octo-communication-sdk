namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Describes a node in the pipeline
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NodeNameAttribute : Attribute
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="name">Name of the node</param>
    /// <param name="version">Version of the node</param>
    public NodeNameAttribute(string name, int version)
    {
        Name = name;
        Version = version;
    }

    /// <summary>
    /// Returns the name of the node
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Describes the version of the node
    /// </summary>
    public int Version { get; set; }
}