using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// Represents the serializer for debug information in the pipeline
/// </summary>
public class PipelineDebugSerializer : IPipelineDebugSerializer
{
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Constructor
    /// </summary>
    public PipelineDebugSerializer()
    {
        _serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public Task<string> SerializeAsync(DebugInformationRoot debugInformationRoot)
    {
        return Task.FromResult(JsonSerializer.Serialize(debugInformationRoot, _serializerOptions));
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, DebugInformationRoot debugInformationRoot)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, debugInformationRoot, _serializerOptions)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DebugInformationRoot> DeserializeAsync(string formattedText)
    {
        var debugInformationRoot =
            JsonSerializer.Deserialize<DebugInformationRoot>(formattedText, _serializerOptions);
        return Task.FromResult(debugInformationRoot ?? throw new Exception("Deserialization failed"));
    }

    /// <inheritdoc />
    public Task<DebugInformationRoot> DeserializeAsync(Stream stream, CancellationToken? cancellationToken = null)
    {
        var debugInformationRoot = JsonSerializer.Deserialize<DebugInformationRoot>(stream, _serializerOptions);
        return Task.FromResult(debugInformationRoot ?? throw new Exception("Deserialization failed"));
    }
}
