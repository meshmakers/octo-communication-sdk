namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

/// <summary>
/// Interface for pipeline configuration serializer
/// </summary>
public interface IPipelineConfigurationSerializer
{
    
    /// <summary>
    /// Serialize pipeline configuration to a string
    /// </summary>
    /// <param name="nodeDefinition">The pipeline object to configure</param>
    /// <returns></returns>
    Task<string> SerializeAsync(NodeDefinitionRoot nodeDefinition);
    
    /// <summary>
    /// Serialize pipeline configuration to a stream
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="nodeDefinition">The pipeline object to configure</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, NodeDefinitionRoot nodeDefinition);


    /// <summary>
    /// Deserializes the pipeline configuration from a string
    /// </summary>
    /// <param name="formattedText">Formatted text to deserialize</param>
    /// <returns></returns>
    Task<NodeDefinitionRoot> DeserializeAsync(string formattedText);
    
    /// <summary>
    ///     Deserializes the pipeline configuration from a stream
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The deserialized object</returns>
    Task<NodeDefinitionRoot> DeserializeAsync(Stream stream, CancellationToken? cancellationToken = null);
}