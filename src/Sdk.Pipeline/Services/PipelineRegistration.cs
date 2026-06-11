using System.Collections.Concurrent;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Represents pipeline registration
/// </summary>
/// <param name="TenantId">Tenant id</param>
/// <param name="DataFlowRtId">Data flow runtime id</param>
/// <param name="PipelineRtEntityId">Pipeline id</param>
/// <param name="IsDebuggingEnabled">When true, the pipeline is running in debug mode</param>
/// <param name="NodeDefinitionRoot">Node definitions, that are used to build the pipeline</param>
/// <param name="GlobalConfiguration">Global configuration</param>
/// <param name="Dictionary">Dictionary shared between execution runs</param>
public record PipelineRegistration(
    string TenantId,
    OctoObjectId DataFlowRtId,
    RtEntityId PipelineRtEntityId,
    bool IsDebuggingEnabled,
    NodeDefinitionRoot NodeDefinitionRoot,
    IGlobalConfiguration GlobalConfiguration,
    Dictionary<string, object?> Dictionary)
{
    /// <summary>
    /// A list of pipeline executions
    /// </summary>
    private readonly ConcurrentDictionary<Guid, PipelineExecution> _pipelineExecutions = new();

    /// <summary>
    /// List of trigger extract nodes
    /// </summary>
    private readonly ConcurrentBag<Tuple<ITriggerPipelineNode, ITriggerContext>> _triggerPipelineNodes = new();

    /// <summary>
    /// Returns true if the pipeline is running in debug mode
    /// </summary>
    public bool IsDebuggingEnabled { get; } = IsDebuggingEnabled;

    /// <summary>
    /// Returns the configuration root
    /// </summary>
    public NodeDefinitionRoot NodeDefinitionRoot { get; } = NodeDefinitionRoot;

    /// <summary>
    ///  Returns a list of global configurations associated to the pipeline
    /// </summary>
    public IGlobalConfiguration GlobalConfiguration { get; } = GlobalConfiguration;

    /// <summary>
    /// Get the execution property value
    /// </summary>
    /// <param name="pipelineExecutionId">ID of the pipeline execution</param>
    /// <param name="key">Key of the property</param>
    /// <typeparam name="T">Type of the property</typeparam>
    /// <returns>Value of the property</returns>
    // ReSharper disable once UnusedMember.Global
    public T? GetExecutionPropertyValue<T>(Guid pipelineExecutionId, string key)
    {
        if (_pipelineExecutions.TryGetValue(pipelineExecutionId, out var pipelineExecution))
        {
            var o = pipelineExecution.Properties[key];
            if (o == null)
            {
                return default;
            }

            return o is T o1 ? o1 : default;
        }

        return default;
    }

    /// <summary>
    /// Registers a pipeline execution
    /// </summary>
    /// <param name="pipelineExecutionId">ID of the pipeline execution</param>
    /// <param name="startedDateTime">Date and time the pipeline execution started</param>
    /// <param name="executePipelineTask"></param>
    // ReSharper disable once UnusedMethodReturnValue.Global
    public PipelineExecution RegisterExecution(Guid pipelineExecutionId, DateTime startedDateTime,
        Task<object?> executePipelineTask)
    {
        var pipelineExecution = new PipelineExecution(pipelineExecutionId, startedDateTime,
            executePipelineTask, new Dictionary<string, object?>());
        _pipelineExecutions.TryAdd(pipelineExecutionId, pipelineExecution);

        // Only allow 10 executions to be stored, but only remove completed ones
        if (_pipelineExecutions.Count > 10)
        {
            var oldestCompleted = _pipelineExecutions
                .Where(x => x.Value.ExecutePipelineTask.IsCompleted)
                .OrderBy(x => x.Value.StartedDateTime)
                .FirstOrDefault();

            if (oldestCompleted.Key != Guid.Empty)
            {
                _pipelineExecutions.TryRemove(oldestCompleted.Key, out _);
            }
        }

        return pipelineExecution;
    }

    /// <summary>
    /// Wait for the pipeline execution to complete and returns the result
    /// </summary>
    /// <param name="pipelineExecutionId">The pipeline execution id</param>
    /// <returns>The result of the pipeline execution</returns>
    public async Task<object?> UnregisterExecutionAsync(Guid pipelineExecutionId)
    {
        if (_pipelineExecutions.TryGetValue(pipelineExecutionId, out var pipelineExecution))
        {
            var result = await pipelineExecution.ExecutePipelineTask;
            _pipelineExecutions.TryRemove(pipelineExecutionId, out _);
            return result;
        }

        throw PipelineExecutionException.PipelineExecutionNotFound(TenantId, PipelineRtEntityId, pipelineExecutionId);
    }

    /// <summary>
    /// Get the status of the pipeline execution
    /// </summary>
    /// <param name="pipelineExecutionId">The pipeline execution id</param>
    /// <returns></returns>
    public PipelineExecutionStatus GetPipelineExecutionStatus(Guid pipelineExecutionId)
    {
        if (_pipelineExecutions.TryGetValue(pipelineExecutionId, out var pipelineExecution))
        {
            if (pipelineExecution.ExecutePipelineTask.IsFaulted)
            {
                return PipelineExecutionStatus.Failed;
            }

            if (pipelineExecution.ExecutePipelineTask.IsCompleted)
            {
                return PipelineExecutionStatus.Completed;
            }

            return PipelineExecutionStatus.Running;
        }

        return PipelineExecutionStatus.Failed;
    }

    /// <summary>
    /// Gets the start time of a pipeline execution.
    /// </summary>
    /// <param name="pipelineExecutionId">The pipeline execution id</param>
    /// <returns>The start time if found, null otherwise</returns>
    public DateTime? GetExecutionStartTime(Guid pipelineExecutionId)
    {
        if (_pipelineExecutions.TryGetValue(pipelineExecutionId, out var pipelineExecution))
        {
            return pipelineExecution.StartedDateTime;
        }

        return null;
    }

    /// <summary>
    /// Register a triggerable extract node
    /// </summary>
    /// <param name="serviceProvider">Global service provider</param>
    public async Task StartTriggerPipelineNodesAsync(IServiceProvider serviceProvider)
    {
        if (NodeDefinitionRoot.Triggers == null)
        {
            throw PipelineExecutionException.PipelineTriggerMissing(TenantId, PipelineRtEntityId);
        }

        if (_triggerPipelineNodes.Any())
        {
            throw PipelineExecutionException.PipelineTriggerAlreadyRegistered(TenantId, PipelineRtEntityId);
        }
        
        var contextCreatorService = serviceProvider.GetRequiredService<IContextCreatorService>();
        var nodeLookupService = serviceProvider.GetRequiredService<INodeLookupService>();
        var logger = serviceProvider.GetRequiredService<IPipelineLogger>();

        foreach (var nodeConfiguration in NodeDefinitionRoot.Triggers)
        {
            if (!nodeLookupService.TryGetNodeConfigurationQualifiedName(nodeConfiguration.GetType(),
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    out var nodeQualifiedName) || nodeQualifiedName == null)
            {
                throw DataPipelineException.UnknownConfigurationType(nodeConfiguration.GetType());
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (!nodeLookupService.TryCreateInstance(serviceProvider, nodeQualifiedName, out var node) || node == null)
            {
                throw DataPipelineException.UnknownObjectPipelineNode(nodeQualifiedName);
            }

            var nodeContext = NodeContext.CreateRootNodeContext(serviceProvider, logger, nodeQualifiedName, nodeConfiguration);
            var triggerContext =
                contextCreatorService.CreateTriggerContext(TenantId, DataFlowRtId, PipelineRtEntityId, nodeContext,
                    GlobalConfiguration);
            try
            {
                await node.StartAsync(triggerContext);
                _triggerPipelineNodes.Add(new Tuple<ITriggerPipelineNode, ITriggerContext>(node, triggerContext));
            }
            catch (Exception e)
            {
                throw PipelineTriggerExecutionException.PipelineRegisterTriggerFailed(TenantId, PipelineRtEntityId,
                    nodeQualifiedName, e);
            }
        }
    }

    /// <summary>
    /// Unregister a triggerable extract node
    /// </summary>
    public async Task StopTriggerPipelineNodesAsync()
    {
        if (NodeDefinitionRoot.Triggers == null)
        {
            throw PipelineExecutionException.PipelineTriggerMissing(TenantId, PipelineRtEntityId);
        }

        foreach (var triggerTuple in _triggerPipelineNodes)
        {
            try
            {
                await triggerTuple.Item1.StopAsync(triggerTuple.Item2);
            }
            catch (Exception e)
            {
                throw PipelineTriggerExecutionException.PipelineUnregisterTriggerFailed(TenantId, PipelineRtEntityId,
                    triggerTuple.Item2.NodeContext.NodePath, e);
            }
        }
    }
}