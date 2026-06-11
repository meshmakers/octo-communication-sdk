using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

/// <summary>
/// Implements the pipeline configuration serializer using YAML
/// </summary>
public class YamlPipelineConfigurationSerializer : IPipelineConfigurationSerializer
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    /// <summary>
    /// Constructor
    /// </summary>
    public YamlPipelineConfigurationSerializer(INodeQualifiedNameLookupService nodeQualifiedNameLookupService)
    {
        _serializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults |
                                            DefaultValuesHandling.OmitEmptyCollections | DefaultValuesHandling.OmitNull)
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkIdAttributeIdConverter())
            .WithTypeConverter(new CkIdTypeIdConverter())
            .WithTypeConverter(new CkIdRecordIdConverter())
            .WithTypeConverter(new CkIdEnumIdConverter())
            .WithTypeConverter(new CkIdAssociationRoleIdConverter())
            .WithTypeConverter(new RtCkIdAttributeIdConverter())
            .WithTypeConverter(new RtCkIdTypeIdConverter())
            .WithTypeConverter(new RtCkIdRecordIdConverter())
            .WithTypeConverter(new RtCkIdEnumIdConverter())
            .WithTypeConverter(new RtCkIdAssociationRoleIdConverter())
            .WithTypeConverter(new OctoObjectIdConverter())
            .WithEmissionPhaseObjectGraphVisitor(args =>
                new NodeConfigurationTypeAppender(args.InnerVisitor, nodeQualifiedNameLookupService))
            .DisableAliases()
            .Build();

        // The deserializer is configured to use the ConfigurationNodeTypeInspector and the ConfigurationNodeTypeDiscriminator
        // to ensure that the correct type is used for deserialization -> The order is important here
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CkIdAttributeIdConverter())
            .WithTypeConverter(new CkIdTypeIdConverter())
            .WithTypeConverter(new CkIdRecordIdConverter())
            .WithTypeConverter(new CkIdEnumIdConverter())
            .WithTypeConverter(new CkIdAssociationRoleIdConverter())
            .WithTypeConverter(new RtCkIdAttributeIdConverter())
            .WithTypeConverter(new RtCkIdTypeIdConverter())
            .WithTypeConverter(new RtCkIdRecordIdConverter())
            .WithTypeConverter(new RtCkIdEnumIdConverter())
            .WithTypeConverter(new RtCkIdAssociationRoleIdConverter())
            .WithTypeConverter(new OctoObjectIdConverter())
            .WithTypeInspector(innerTypeInspector => new NodeConfigurationTypeInspector(innerTypeInspector))
            .WithTypeDiscriminatingNodeDeserializer(o =>
            {
                o.AddTypeDiscriminator(new NodeConfigurationTypeDiscriminator(nodeQualifiedNameLookupService));
            })
            .Build();
    }

    /// <inheritdoc />
    public Task<string> SerializeAsync(NodeDefinitionRoot nodeDefinition)
    {
        try
        {
            return Task.FromResult(_serializer.Serialize(nodeDefinition));
        }
        catch (Exception e)
        {
            throw PipelineSerializationException.SerializeError(e);
        }
    }

    /// <inheritdoc />
    public Task SerializeAsync(StreamWriter streamWriter, NodeDefinitionRoot nodeDefinition)
    {
        try
        {
            _serializer.Serialize(streamWriter, nodeDefinition);
            return Task.CompletedTask;
        }
        catch (Exception e)
        {
            throw PipelineSerializationException.SerializeError(e);
        }
    }

    /// <inheritdoc />
    public Task<NodeDefinitionRoot> DeserializeAsync(string formattedText)
    {
        try
        {
            var configurationRoot = _deserializer.Deserialize<NodeDefinitionRoot>(formattedText);
            return Task.FromResult(configurationRoot);
        }
        catch (Exception e)
        {
            throw PipelineSerializationException.DeserializeError(e);
        }
    }

    /// <inheritdoc />
    public Task<NodeDefinitionRoot> DeserializeAsync(Stream stream, CancellationToken? cancellationToken = null)
    {
        try
        {
            using var streamReader = new StreamReader(stream);
            var configurationRoot = _deserializer.Deserialize<NodeDefinitionRoot>(streamReader);
            return Task.FromResult(configurationRoot);
        }
        catch (Exception e)
        {
            throw PipelineSerializationException.DeserializeError(e);
        }
    }
}