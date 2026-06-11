using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization.BufferedDeserialization.TypeDiscriminators;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

/// <summary>
/// This implementation of <see cref="ITypeDiscriminator"/> is used to determine the correct type of the <see cref="NodeConfiguration"/>
/// based on property Type.
/// </summary>
/// <param name="nodeLookupService">The service to look up the qualified name of the configuration node</param>
internal class NodeConfigurationTypeDiscriminator(INodeQualifiedNameLookupService nodeLookupService) : ITypeDiscriminator
{
    public bool TryDiscriminate(IParser buffer, out Type? suggestedType)
    {
        if (buffer.TryFindMappingEntry(s => s.Value == YamlFields.Type, out _, out var value))
        {
            if (value is Scalar scalarValue)
            {
                if (nodeLookupService.TryGetConfigurationNodeType(scalarValue.Value, out var mappedType))
                {
                    suggestedType = mappedType;
                    return true;
                }
                throw DataPipelineException.UnknownDiscriminator(scalarValue.Value);
            }
        }

        suggestedType = null;
        return false;
    }

    public Type BaseType => typeof(NodeConfiguration);
}