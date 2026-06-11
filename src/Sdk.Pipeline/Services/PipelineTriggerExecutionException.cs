using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Exception thrown when a pipeline trigger execution fails
/// </summary>
public class PipelineTriggerExecutionException : PipelineExecutionException
{
    /// <inheritdoc />
    public PipelineTriggerExecutionException()
    {
    }

    /// <inheritdoc />
    // ReSharper disable once MemberCanBePrivate.Global
    public PipelineTriggerExecutionException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    // ReSharper disable once MemberCanBePrivate.Global
    public PipelineTriggerExecutionException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>
    /// Exception thrown when a pipeline trigger registration fails
    /// </summary>
    /// <returns></returns>
    public static Exception PipelineRegisterTriggerFailed(string tenantId, RtEntityId pipelineRtEntityId,
        string nodeQualifiedName, Exception exception)
    {
        return new PipelineTriggerExecutionException(
            $"[{tenantId}] Pipeline '{pipelineRtEntityId}' trigger registration failed for node '{nodeQualifiedName}'",
            exception);
    }

    /// <summary>
    /// Exception thrown when a pipeline trigger unregistration fails
    /// </summary>
    /// <returns></returns>
    public static Exception PipelineUnregisterTriggerFailed(string tenantId, RtEntityId pipelineRtEntityId,
        NodePath nodeContextNodePath, Exception exception)
    {
        return new PipelineExecutionException(
            $"[{tenantId}] Pipeline '{pipelineRtEntityId}' trigger unregistration failed for node '{nodeContextNodePath}'",
            exception);
    }
}