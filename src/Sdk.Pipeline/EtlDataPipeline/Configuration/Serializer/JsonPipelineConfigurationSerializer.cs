using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

/// <summary>
/// Implements the pipeline configuration serializer using JSON format
/// </summary>
public class JsonPipelineConfigurationSerializer : IJsonPipelineConfigurationSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Constructor
    /// </summary>
    public JsonPipelineConfigurationSerializer(INodeQualifiedNameLookupService nodeQualifiedNameLookupService)
    {
        _options = new JsonSerializerOptions
        {
            Converters =
            {
                new NodeConfigurationConverter<NodeConfiguration>(nodeQualifiedNameLookupService)
            },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    /// <inheritdoc />
    public Task<string> SerializeAsync(NodeDefinitionRoot nodeDefinition)
    {
        try
        {
            return Task.FromResult(JsonSerializer.Serialize(nodeDefinition, _options));
        }
        catch (Exception e)
        {
            throw PipelineSerializationException.SerializeError(e);
        }
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, NodeDefinitionRoot nodeDefinition)
    {
        try
        {
            await JsonSerializer.SerializeAsync(streamWriter.BaseStream, nodeDefinition, _options)
                .ConfigureAwait(false);
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
            var pipelineConfigurationRoot =
                JsonSerializer.Deserialize<NodeDefinitionRoot>(formattedText, _options);
            return Task.FromResult(pipelineConfigurationRoot ?? throw new Exception("Deserialization failed"));
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
            var pipelineConfigurationRoot = JsonSerializer.Deserialize<NodeDefinitionRoot>(stream, _options);
            return Task.FromResult(pipelineConfigurationRoot ?? throw new Exception("Deserialization failed"));
        }
        catch (Exception e)
        {
            throw PipelineSerializationException.DeserializeError(e);
        }
    }
}