namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Registry that provides node descriptors with JSON schemas for all registered pipeline node types.
/// </summary>
public interface INodeSchemaRegistry
{
    /// <summary>
    /// Gets all registered node descriptors.
    /// </summary>
    IReadOnlyList<NodeDescriptor> GetAllDescriptors();

    /// <summary>
    /// Gets a node descriptor by its qualified name (e.g. "Select@1").
    /// </summary>
    /// <param name="qualifiedName">The qualified name of the node</param>
    /// <returns>The node descriptor, or null if not found</returns>
    NodeDescriptor? GetDescriptor(string qualifiedName);
}
