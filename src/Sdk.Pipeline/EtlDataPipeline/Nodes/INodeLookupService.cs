using System.Diagnostics.CodeAnalysis;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

/// <summary>
/// Service for looking up pipeline nodes by their configuration qualified name.
/// </summary>
public interface INodeLookupService
{
    /// <summary>
    /// Trie to create an instance of a pipeline node by its configuration qualified name.
    /// </summary>
    /// <param name="serviceProvider">Serviceprovider</param>
    /// <param name="nodeQualifiedName">Name of the node including version.</param>
    /// <param name="next">Delegate to the next node in the pipeline.</param>
    /// <param name="pipelineNode">The pipeline node.</param>
    /// <returns>The pipeline node if it was found and instance is created, otherwise null.</returns>
#if !NETSTANDARD2_0
    bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, NodeDelegate next, [NotNullWhen(true)] out IPipelineNode? pipelineNode);
#else
    bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, NodeDelegate next, out IPipelineNode? pipelineNode);
#endif
    
    /// <summary>
    /// Trie to create an instance of a pipeline node by its configuration qualified name.
    /// </summary>
    /// <param name="serviceProvider">Serviceprovider</param>
    /// <param name="nodeQualifiedName">Name of the node including version.</param>
    /// <param name="pipelineNode">The pipeline node.</param>
    /// <returns>The pipeline node if it was found and instance is created, otherwise null.</returns>
#if !NETSTANDARD2_0
    bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, [NotNullWhen(true)] out ITriggerPipelineNode? pipelineNode);
#else
    bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, out ITriggerPipelineNode? pipelineNode);
#endif
    
    /// <summary>
    /// Try to get the configuration qualified name of a node type.
    /// </summary>
    /// <param name="configurationNodeType">Configuration node type.</param>
    /// <param name="qualifiedName">The configuration qualified name.</param>
    /// <returns>True if the object pipeline node was found, otherwise false.</returns>
#if !NETSTANDARD2_0
    bool TryGetNodeConfigurationQualifiedName(Type configurationNodeType, [NotNullWhen(true)] out string? qualifiedName);
#else
    bool TryGetNodeConfigurationQualifiedName(Type configurationNodeType, out string? qualifiedName);
#endif
    
}