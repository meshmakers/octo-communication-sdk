using System.Diagnostics.CodeAnalysis;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

internal class NodeQualifiedNameLookupService : INodeQualifiedNameLookupService
{
    private readonly Dictionary<string, Type> _byName;
    private readonly Dictionary<Type, string> _byConfigType;
    
    public NodeQualifiedNameLookupService(IDictionary<string, Type> nodeTypes)
    {
        _byName = new Dictionary<string, Type>(nodeTypes);
        _byConfigType = nodeTypes.ToDictionary(x => x.Value, x => x.Key);
    }

#if !NETSTANDARD2_0
    public bool TryGetConfigurationNodeType(string nodeQualifiedName, [NotNullWhen(true)] out Type? nodeConfigurationType) 
#else
    public bool TryGetConfigurationNodeType(string nodeQualifiedName, out Type? nodeConfigurationType)
#endif
    {
        if (_byName.TryGetValue(nodeQualifiedName, out nodeConfigurationType))
        {
            return true;
        }

        nodeConfigurationType = null;
        return false;
    }
#if !NETSTANDARD2_0
    public bool TryGetNodeConfigurationQualifiedName(Type configurationNodeType, [NotNullWhen(true)] out string? qualifiedName) 
#else
    public bool TryGetNodeConfigurationQualifiedName(Type configurationNodeType, out string? qualifiedName)
#endif
    
    {
        if (_byConfigType.TryGetValue(configurationNodeType, out qualifiedName))
        {
            return true;
        }

        qualifiedName = null;
        return false;
    }
}