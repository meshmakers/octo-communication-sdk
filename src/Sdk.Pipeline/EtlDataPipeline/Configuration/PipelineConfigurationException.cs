namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Handles exceptions related to pipeline configuration
/// </summary>
public class PipelineConfigurationException : Exception
{
    /// <inheritdoc />
    public PipelineConfigurationException()
    {
    }

    /// <inheritdoc />
    public PipelineConfigurationException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public PipelineConfigurationException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception InvalidTriggerNode(Type nodeType)
    {
        return new PipelineConfigurationException($"Node '{nodeType.FullName}' is not a valid trigger node");
    }

    internal static Exception InvalidNodeType(Type nodeType)
    {
        return new PipelineConfigurationException($"Node '{nodeType.FullName}' is not a valid node");
    }

    internal static Exception InvalidOperation(string s)
    {
        return new PipelineConfigurationException($"Invalid operation: {s}");
    }
}