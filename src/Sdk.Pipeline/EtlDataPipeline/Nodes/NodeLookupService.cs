using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

internal class NodeLookupService : INodeLookupService
{
    private readonly Dictionary<string, NodeLookup> _byName;
    private readonly Dictionary<Type, NodeLookup> _byConfigType;
    
    public NodeLookupService(List<NodeLookup> nodeLookups)
    {
        _byName = nodeLookups.ToDictionary(x => x.QualifiedName, x => x);
        _byConfigType = nodeLookups.ToDictionary(x => x.NodeConfigurationType, x => x);
    }

#if !NETSTANDARD2_0
    public bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, NodeDelegate next, [NotNullWhen(true)] out IPipelineNode? pipelineNode)
#else
    public bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, NodeDelegate next, out IPipelineNode? pipelineNode)
#endif
    
    {
        if (_byName.TryGetValue(nodeQualifiedName, out var nodeLookup))
        {
            pipelineNode = (IPipelineNode?)ActivatorUtilities.CreateInstance(serviceProvider, nodeLookup.NodeType, next);
            if (pipelineNode == null)
            {
                throw DataPipelineException.CannotCreateInstance(nodeLookup.NodeType);
            }
            return true;
        }

        pipelineNode = null;
        return false;
    }

#if !NETSTANDARD2_0
    public bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, [NotNullWhen(true)] out ITriggerPipelineNode? pipelineNode)
#else
    public bool TryCreateInstance(IServiceProvider serviceProvider, string nodeQualifiedName, out ITriggerPipelineNode? pipelineNode)
#endif

    {
        if (_byName.TryGetValue(nodeQualifiedName, out var nodeLookup))
        {
            pipelineNode = (ITriggerPipelineNode?)ActivatorUtilities.CreateInstance(serviceProvider, nodeLookup.NodeType);
            if (pipelineNode == null)
            {
                throw DataPipelineException.CannotCreateInstance(nodeLookup.NodeType);
            }
            return true;
        }

        pipelineNode = null;
        return false;
    }
    
#if !NETSTANDARD2_0
    public bool TryGetNodeConfigurationQualifiedName(Type configurationNodeType, [NotNullWhen(true)] out string? qualifiedName)
#else
    public bool TryGetNodeConfigurationQualifiedName(Type configurationNodeType, out string? qualifiedName)
#endif
    {
        if (_byConfigType.TryGetValue(configurationNodeType, out var nodeLookup))
        {
            qualifiedName = nodeLookup.QualifiedName;
            return true;
        }

        qualifiedName = null;
        return false;
    }
}