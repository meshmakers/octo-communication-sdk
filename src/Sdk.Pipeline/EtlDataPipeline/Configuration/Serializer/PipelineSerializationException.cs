using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

/// <summary>
/// Exception thrown when there is an error serializing or deserializing a pipeline.
/// </summary>
public class PipelineSerializationException : DataPipelineException
{
    /// <summary>
    /// Constructor.
    /// </summary>
    public PipelineSerializationException()
    {
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    public PipelineSerializationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    public PipelineSerializationException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception DeserializeError(Exception exception)
    {
        string message = "Error deserializing pipeline: " + exception.GetDirectAndIndirectMessages();
        return new PipelineSerializationException(message, exception);
    }

    internal static Exception SerializeError(Exception exception)
    {
        string message = "Error serializing pipeline: " + exception.GetDirectAndIndirectMessages();
        return new PipelineSerializationException(message, exception);
    }
}