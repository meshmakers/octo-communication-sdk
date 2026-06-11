using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectGraphVisitors;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

/// <summary>
/// This <seealso cref="IObjectGraphVisitor{TContext}"/> ensures that the type of the configuration node is emitted as a scalar to be
/// deserializable by the <see cref="NodeConfigurationTypeDiscriminator"/>.
/// </summary>
/// <param name="nextVisitor">The next visitor in the chain</param>
/// <param name="nodeLookupService">The service to look up the qualified name of the configuration node</param>
internal class NodeConfigurationTypeAppender(IObjectGraphVisitor<IEmitter> nextVisitor, INodeQualifiedNameLookupService nodeLookupService) 
    : ChainedObjectGraphVisitor(nextVisitor)
{
    public override void VisitMappingStart(IObjectDescriptor mapping, Type keyType, Type valueType, IEmitter context,
        ObjectSerializer serializer)
    {
        base.VisitMappingStart(mapping, keyType, valueType, context, serializer);
        if (typeof(NodeConfiguration).IsAssignableFrom(mapping.Type))
        {
            if (nodeLookupService.TryGetNodeConfigurationQualifiedName(mapping.Type, out var nodeQualifiedName))
            {
                context.Emit(new Scalar(null, YamlFields.Type));
                context.Emit(new Scalar(null, nodeQualifiedName!));
            }
            else
            {
                throw DataPipelineException.UnknownDiscriminator(mapping.Type.GetConfigurationQualifiedName());
            }
        }
    }
}