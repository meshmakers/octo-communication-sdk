namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// Interface for pipeline debug serializer
/// </summary>
public interface IPipelineDebugSerializer
{
    /// <summary>
    /// Serializes the debug information root to a string
    /// </summary>
    /// <param name="debugInformationRoot">Debug information root to serialize</param>
    /// <returns></returns>
    Task<string> SerializeAsync(DebugInformationRoot debugInformationRoot);

    /// <summary>
    /// Serializes the debug information root to a stream
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="debugInformationRoot">Debug information root to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, DebugInformationRoot debugInformationRoot);

    /// <summary>
    /// Deserializes the debug information root from a string
    /// </summary>
    /// <param name="formattedText">Formatted text to deserialize</param>
    /// <returns>Deserialized debug information root</returns>
    Task<DebugInformationRoot> DeserializeAsync(string formattedText);

    /// <summary>
    /// Deserializes the debug information root from a stream
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Deserialized debug information root</returns>
    Task<DebugInformationRoot> DeserializeAsync(Stream stream, CancellationToken? cancellationToken = null);
}