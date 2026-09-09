using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Exception thrown when a pipeline execution fails
/// </summary>
public class PipelineExecutionException : Exception
{
    /// <inheritdoc />
    public PipelineExecutionException()
    {
    }

    /// <inheritdoc />
    // ReSharper disable once MemberCanBePrivate.Global
    public PipelineExecutionException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    // ReSharper disable once MemberCanBePrivate.Global
    public PipelineExecutionException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>
    /// Exception thrown when a pipeline is not found 
    /// </summary>
    public static Exception PipelineNotFound(string tenantId, RtEntityId pipelineRtEntityId)
    {
        return new PipelineExecutionException($"[{tenantId}]Pipeline '{pipelineRtEntityId}' not found");
    }

    /// <summary>
    /// Exception thrown when a pipeline execution is not found
    /// </summary>
    public static Exception PipelineExecutionNotFound(string tenantId, RtEntityId pipelineRtEntityId,
        Guid pipelineExecutionId)
    {
        return new PipelineExecutionException(
            $"[{tenantId}] Pipeline '{pipelineRtEntityId}' execution '{pipelineExecutionId}' not found");
    }

    /// <summary>
    /// Exception thrown when a pipeline trigger is missing
    /// </summary>
    public static Exception PipelineTriggerMissing(string tenantId, RtEntityId pipelineRtEntityId)
    {
        return new PipelineExecutionException($"[{tenantId}] Pipeline '{pipelineRtEntityId}' trigger missing");
    }

    /// <summary>
    /// Exception thrown when a pipeline trigger is already registered
    /// </summary>
    /// <returns></returns>
    public static Exception PipelineTriggerAlreadyRegistered(string tenantId, RtEntityId pipelineRtEntityId)
    {
        return new PipelineExecutionException(
            $"[{tenantId}] Pipeline '{pipelineRtEntityId}' trigger already registered");
    }



    /// <summary>
    /// Exception thrown when a pipeline registration fails
    /// </summary>
    /// <returns></returns>
    public static Exception PipelineRegistrationFailed(string tenantId, List<string> errorMessages)
    {
        return new PipelineExecutionException(
            $"[{tenantId}] Pipeline registration failed: {string.Join(Environment.NewLine, errorMessages)}");
    }

    /// <summary>
    /// Exception thrown when a pipeline trigger start fails
    /// </summary>
    /// <returns></returns>
    public static Exception StartTriggerPipelineNodesFailed(string tenantId, List<string> errorMessages)
    {
        return new PipelineExecutionException(
            $"[{tenantId}] Pipeline registration failed: {string.Join(Environment.NewLine, errorMessages)}");
    }

    /// <summary>
    /// Exception thrown when a pipeline trigger end fails
    /// </summary>
    /// <returns></returns>
    public static Exception EtlContextTypeMismatch<TContext>(IEtlContext context) where TContext : class, IEtlContext
    {
        return new PipelineExecutionException(
            $"Etl context type mismatch. Expected {typeof(TContext).Name} but got {context.GetType().Name}");
    }

    /// <summary>
    /// Exception thrown when a global configuration parameter is not found
    /// </summary>
    /// <param name="configurationName">Configuration name</param>
    /// <returns></returns>
    public static Exception GlobalConfigurationParameterNotFound(string configurationName)
    {
        return new PipelineExecutionException($"Global configuration parameter '{configurationName}' not found");
    }

    /// <summary>
    /// Exception thrown when a parent property is not found
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="fcPath">Path to the field</param>
    /// <returns></returns>
    public static Exception ParentPropertyNotFound(NodePath nodePath, string fcPath)
    {
        return new PipelineExecutionException($"[{nodePath}]: Parent property not found for field {fcPath}");
    }

    /// <summary>
    /// Exception thrown when a value is not an array
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="configurationPropertyName">Name of the configuration property</param>
    /// <param name="path">Path to the value</param>
    /// <returns></returns>
    public static Exception PathMustBeArray(string nodePath, string configurationPropertyName, string path)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Configuration property '{configurationPropertyName}' defines '{path}', but the value in the pipeline is not an array");
    }

    /// <summary>
    /// Exception thrown when a value is not an array
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="path">Path to the value</param>
    /// <returns></returns>
    public static Exception PathNotFound(NodePath nodePath, string path)
    {
        return new PipelineExecutionException($"[{nodePath}]: Path '{path}' not found");
    }

    /// <summary>
    /// Exception thrown when a value type is not supported
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="valueType">Value type that is not supported</param>
    /// <param name="path">Path the value type has been loaded from</param>
    /// <returns></returns>
    public static Exception ValueTypeNotSupported(NodePath nodePath, AttributeValueTypesDto valueType, string path)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Value type '{valueType}' is not supported for path '{path}'.");
    }

    /// <summary>
    /// Exception thrown when a value type is not supported
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="valueType">Value type that is not supported</param>
    /// <param name="value">Value that is not supported</param>
    /// <returns></returns>
    public static Exception DefinedValueTypeNotSupported(NodePath nodePath, AttributeValueTypesDto valueType, object? value)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Value type '{valueType}' is not supported to convert. Defined value '{value}'.");
    }

    /// <summary>
    /// Exception thrown when a value is not set
    /// </summary>
    /// <param name="nodeContext">Node context</param>
    /// <returns></returns>
    public static Exception InputValueNull(INodeContext nodeContext)
    {
        return new PipelineExecutionException($"[{nodeContext.NodePath}]: Input value is null");
    }

    /// <summary>
    /// Exception thrown when a value is not set
    /// </summary>
    /// <param name="nodeContext">Node context</param>
    /// <param name="valuePath">Path to the value that is not set</param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static Exception ValueNotSet(INodeContext nodeContext, string? valuePath)
    {
        return new PipelineExecutionException(
            $"[{nodeContext.NodePath}]: Value not set. Value path: '{valuePath ?? "<not defined>"}'");
    }

    /// <summary>
    /// Exception thrown when iterations of a loop node failed while the loop continued on error.
    /// The message names only the failed indices; the child exceptions (whose messages may carry
    /// item payload content) travel in the inner <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="totalCount">Total number of iterations</param>
    /// <param name="failures">Failed iterations with their source-array indices, ordered by index</param>
    /// <returns></returns>
    public static Exception IterationsFailed(NodePath nodePath, int totalCount,
        IReadOnlyCollection<(uint Index, Exception Error)> failures)
    {
        const int maxDetails = 5;
        var details = string.Join(", ", failures.Take(maxDetails).Select(f => $"[{f.Index}]"));
        var more = failures.Count > maxDetails ? $"; +{failures.Count - maxDetails} more" : string.Empty;
        return new PipelineExecutionException(
            $"[{nodePath}]: {failures.Count} of {totalCount} iterations failed. Failed indices: {details}{more}",
            new AggregateException(failures.Select(f => f.Error)));
    }

    /// <summary>
    /// Exception thrown when a configuration property is only valid together with another one
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="configurationPropertyName">Name of the configured property</param>
    /// <param name="requiredPropertyName">Name of the property it requires</param>
    /// <returns></returns>
    public static Exception ConfigurationPropertyRequires(NodePath nodePath, string configurationPropertyName,
        string requiredPropertyName)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Configuration property '{configurationPropertyName}' requires '{requiredPropertyName}' to be enabled");
    }

    /// <summary>
    /// Exception thrown when a configuration property is set but carries no value
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="configurationPropertyName">Name of the configured property</param>
    /// <returns></returns>
    public static Exception ConfigurationPropertyEmpty(NodePath nodePath, string configurationPropertyName)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Configuration property '{configurationPropertyName}' is set but empty");
    }

    /// <summary>
    /// Exception thrown when a configuration property contains a path that cannot be written to
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="configurationPropertyName">Name of the configuration property</param>
    /// <param name="reason">Why the path is not writable; carries the offending path</param>
    /// <returns></returns>
    public static Exception ConfigurationPropertyPathInvalid(NodePath nodePath, string configurationPropertyName,
        string reason)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Configuration property '{configurationPropertyName}' contains an invalid path: {reason}");
    }

    /// <summary>
    /// Exception thrown when a configured time zone id cannot be resolved on the running system
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="timeZoneId">The time zone id that could not be resolved</param>
    /// <returns></returns>
    public static Exception TimeZoneNotFound(NodePath nodePath, string timeZoneId)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Time zone '{timeZoneId}' not found. Expected an IANA time zone id, e.g. 'Europe/Vienna'.");
    }

    /// <summary>
    /// Exception thrown when a value cannot be read as Unix time in milliseconds
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="path">Path the value was read from</param>
    /// <param name="reason">Why the value is not a usable Unix timestamp</param>
    /// <param name="inner">The underlying conversion or range error</param>
    /// <returns></returns>
    public static Exception InvalidUnixTimestamp(NodePath nodePath, string path, string reason, Exception inner)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Value at '{path}' is not a valid Unix time in milliseconds: {reason}", inner);
    }

    /// <summary>
    /// Exception thrown when two configuration properties address overlapping paths
    /// </summary>
    /// <param name="nodePath">Path to the node</param>
    /// <param name="firstPropertyName">Name of the first configuration property</param>
    /// <param name="secondPropertyName">Name of the second configuration property</param>
    /// <param name="firstPath">Path the first property addresses</param>
    /// <param name="secondPath">Path the second property addresses</param>
    /// <returns></returns>
    public static Exception ConfigurationPropertyPathsMustNotOverlap(NodePath nodePath, string firstPropertyName,
        string secondPropertyName, string firstPath, string secondPath)
    {
        return new PipelineExecutionException(
            $"[{nodePath}]: Configuration properties '{firstPropertyName}' ('{firstPath}') and '{secondPropertyName}' ('{secondPath}') must not address overlapping paths");
    }
}
