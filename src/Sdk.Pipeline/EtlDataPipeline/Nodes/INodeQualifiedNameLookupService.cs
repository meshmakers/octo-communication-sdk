using System.Diagnostics.CodeAnalysis;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

/// <summary>
/// Interface for looking up pipeline nodes by their configuration qualified name.
/// </summary>
public interface INodeQualifiedNameLookupService
{
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
    
    
    /// <summary>
    /// Try to get the configuration node type by its qualified name.
    /// </summary>
    /// <param name="nodeQualifiedName">The configuration qualified name.</param>
    /// <param name="nodeConfigurationType">Configuration node type.</param>
    /// <returns>True if the object pipeline node was found, otherwise false.</returns>
#if !NETSTANDARD2_0
    bool TryGetConfigurationNodeType(string nodeQualifiedName, [NotNullWhen(true)] out Type? nodeConfigurationType); 
#else
    bool TryGetConfigurationNodeType(string nodeQualifiedName, out Type? nodeConfigurationType);
#endif
    
}