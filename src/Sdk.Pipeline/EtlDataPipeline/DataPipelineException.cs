using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using YamlDotNet.Core;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Exception thrown by the data pipeline.
/// </summary>
public class DataPipelineException : Exception
{
    /// <summary>
    /// Constructor.
    /// </summary>
    protected DataPipelineException()
    {
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="message"></param>
    protected DataPipelineException(string message) : base(message)
    {
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="inner"></param>
    protected DataPipelineException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception MissingRequiredConfiguration(string nodeName, string propertyName)
    {
        return new DataPipelineException($"Required property '{propertyName}' is not set on node '{nodeName}'.");
    }

    internal static Exception NoConfigurationNodeSet()
    {
        return new DataPipelineException("No configuration node set.");
    }

    internal static Exception ValueTypeUnsupported(string path, AttributeValueTypesDto valueType)
    {
        return new DataPipelineException($"Value type '{valueType}' is not supported for path '{path}'.");
    }

    internal static Exception UnknownDiscriminator(string discriminatorValue)
    {
        return new DataPipelineException($"Unknown discriminator '{discriminatorValue}'.");
    }
    
    internal static Exception UnknownConfigurationType(Type type)
    {
        return new DataPipelineException($"Unknown configuration type '{type.FullName}'.");
    }

    internal static Exception UnknownObjectPipelineNode(string nodeQualifiedName)
    {
        return new DataPipelineException($"Unknown object pipeline node '{nodeQualifiedName}'.");
    }

    internal static Exception CurrentIsNull()
    {
        return new DataPipelineException("Current is null.");
    }

    internal static Exception ValueIsObjectButMustBePrimitive(string? path)
    {
        return new DataPipelineException($"Value at path '{path}' is an object but must be a primitive.");
    }

    internal static Exception NoDiscriminatorFound()
    {
        return new DataPipelineException("No discriminator found.");
    }

    internal static Exception CannotCreateInstance(Type nodeType)
    {
        return new DataPipelineException($"Cannot create instance of type '{nodeType.FullName}'.");
    }

    internal static Exception ValueIsArrayMustBeScalar(string? path)
    {
        return new DataPipelineException($"Value at path '{path}' is an array but must be a scalar.");
    }

    internal static Exception ValueIsObjectButMustBeArray(string? path)
    {
        return new DataPipelineException($"Value at path '{path}' is an object but must be an array.");
    }

    internal static Exception ValueIsNotArray(string path)
    {
        return new DataPipelineException($"Value at path '{path}' is not an array.");
    }

    internal static Exception EtlContextFactoryNotSet(Type t)
    {
        return new DataPipelineException($"EtlContextFactory for '{t.Name}'not set.");
    }

    internal static Exception SourceMustBeAnObject(string sourcePath, DataKind currentKind)
    {
        return new DataPipelineException($"Source at path '{sourcePath}' must be an object. Current kind is '{currentKind}'.");
    }

    internal static Exception UnknownWriteMode(TargetValueWriteModes targetValueWriteModes)
    {
        return new DataPipelineException($"Unknown write mode '{targetValueWriteModes}'.");
    }

    internal static Exception ValueIsArrayMustBeScalarForWriteMode(string path, TargetValueWriteModes targetValueWriteModes)
    {
        return new DataPipelineException($"Value at path '{path}' is an array but must be a scalar for write mode '{targetValueWriteModes}'.");
    }

    internal static Exception ParentNodeContextIsNull()
    {
        return new DataPipelineException("Parent node context is null.");
    }

    internal static Exception TargetValueIsObjectMustBeObjectForWriteMode(string path, TargetValueWriteModes targetValueWriteModes)
    {
        return new DataPipelineException($"Target value at path '{path}' is an object but must be an object for write mode '{targetValueWriteModes}'.");
    }

    internal static Exception SourceValueIsObjectMustBeObjectForWriteMode(string path, TargetValueWriteModes targetValueWriteModes)
    {
        return new DataPipelineException($"Source value at path '{path}' is an object but must be an object for write mode '{targetValueWriteModes}'.");
    }

    internal static Exception TargetPipelineFailed(string? errorMessage)
    {
        return new DataPipelineException($"Target pipeline failed: {errorMessage}");
    }

    internal static Exception NodeExecutionFailed(string nodePath, Exception innerException)
    {
        return new DataPipelineException($"Error in node '{nodePath}': {innerException.Message}", innerException);
    }
}
