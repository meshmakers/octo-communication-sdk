using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Implementation of the pipeline execution service
/// </summary>
public sealed class PipelineRegistryService(
    ILogger<PipelineRegistryService> logger,
    IServiceProvider serviceProvider,
    IPipelineConfigurationSerializer pipelineConfigurationSerializer)
    : IPipelineRegistryService
{
    private readonly ConcurrentDictionary<Tuple<string, RtEntityId>, PipelineRegistration> _pipelineRegistrationsById =
        new();

    private readonly ConcurrentDictionary<Tuple<string, OctoObjectId>, ICollection<PipelineRegistration>>
        _pipelineRegistrationsByDataFlowId = new();

    private readonly ConcurrentDictionary<Tuple<string, RtEntityId>, PipelineConfigurationDto>
        _pipelineConfigurationsById = new();

    /// <inheritdoc />
    public async Task RegisterPipelineAsync(string tenantId, PipelineConfigurationDto pipelineConfiguration)
    {
        logger.LogInformation(
            "Registering pipeline. TenantId: {TenantId}, PipelineRtEntityId: {PipelineRtEntityId}, DataFlowRtId: {DataFlowRtId}",
            tenantId, pipelineConfiguration.PipelineRtEntityId, pipelineConfiguration.DataFlowRtId);

        // Load and check configuration
        var configurationRoot =
            await pipelineConfigurationSerializer.DeserializeAsync(pipelineConfiguration.NodeConfiguration);

        if (configurationRoot.Triggers == null)
        {
            throw PipelineExecutionException.PipelineTriggerMissing(tenantId, pipelineConfiguration.PipelineRtEntityId);
        }

        var globalConfiguration = new GlobalConfiguration(pipelineConfiguration.Configurations);

        // Register pipeline
        var pipelineRegistration = new PipelineRegistration(tenantId, pipelineConfiguration.DataFlowRtId,
            pipelineConfiguration.PipelineRtEntityId,
            pipelineConfiguration.IsDebuggingEnabled, configurationRoot, globalConfiguration,
            new Dictionary<string, object?>());

        // Start trigger nodes
        await pipelineRegistration.StartTriggerPipelineNodesAsync(serviceProvider);

        var byIdKey = CreateByIdKey(tenantId, pipelineConfiguration.PipelineRtEntityId);
        _pipelineRegistrationsById.TryAdd(byIdKey, pipelineRegistration);
        _pipelineConfigurationsById[byIdKey] = pipelineConfiguration;
        var list = _pipelineRegistrationsByDataFlowId.GetOrAdd(
            CreateDataFlowIdKey(tenantId, pipelineConfiguration.DataFlowRtId),
            new List<PipelineRegistration>());
        list.Add(pipelineRegistration);
    }

    /// <inheritdoc />
    public async Task<bool> RegisterPipelinesAsync(string tenantId,
        ICollection<PipelineConfigurationDto> pipelineConfigurations,
        List<DeploymentUpdateErrorMessageDto> deploymentErrorMessages)
    {
        _pipelineRegistrationsById.Clear();
        _pipelineRegistrationsByDataFlowId.Clear();
        _pipelineConfigurationsById.Clear();

        logger.LogInformation(
            "Registering multiple pipelines for tenant {TenantId}. Pipeline count: {PipelineCount}",
            tenantId, pipelineConfigurations.Count);

        bool success = true;
        foreach (var pipelineConfiguration in pipelineConfigurations)
        {
            try
            {
                await RegisterPipelineAsync(tenantId, pipelineConfiguration);
            }
            catch (PipelineSerializationException e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.PipelineDeserializationError,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
            catch (PipelineTriggerExecutionException e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.PipelineTriggerExecutionError,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
            catch (PipelineExecutionException e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.PipelineInitializationError,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
            catch (Exception e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.Uncategorized,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
        }

        return success;
    }

    /// <inheritdoc />
    public async Task UnregisterPipelineAsync(string tenantId, RtEntityId pipelineRtEntityId)
    {
        var byIdKey = CreateByIdKey(tenantId, pipelineRtEntityId);
        _pipelineConfigurationsById.TryRemove(byIdKey, out _);
        if (_pipelineRegistrationsById.TryRemove(byIdKey, out var pipelineExecutionItem))
        {
            var dataPipelineIdKey = CreateDataFlowIdKey(tenantId, pipelineExecutionItem.DataFlowRtId);
            if (_pipelineRegistrationsByDataFlowId.TryGetValue(dataPipelineIdKey, out var list))
            {
                list.Remove(pipelineExecutionItem);
                if (list.Count == 0)
                {
                    _pipelineRegistrationsByDataFlowId.TryRemove(dataPipelineIdKey, out _);
                }
            }

            await pipelineExecutionItem.StopTriggerPipelineNodesAsync();
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAllPipelinesAsync(string tenantId)
    {
        foreach (var kvp in _pipelineRegistrationsByDataFlowId.Where(x =>
                     x.Key.Item1 == tenantId.NormalizeString()))
        {
            var pipelineExecutionItems = kvp.Value.ToArray();

            foreach (var pipelineExecutionItem in pipelineExecutionItems)
            {
                await UnregisterPipelineAsync(tenantId, pipelineExecutionItem.PipelineRtEntityId);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdatePipelinesAsync(string tenantId,
        ICollection<PipelineConfigurationDto> pipelineConfigurations,
        List<DeploymentUpdateErrorMessageDto> deploymentErrorMessages)
    {
        // Build lookup of new configurations by PipelineRtEntityId
        var newConfigsByPipelineId = new Dictionary<RtEntityId, PipelineConfigurationDto>();
        foreach (var config in pipelineConfigurations)
        {
            newConfigsByPipelineId[config.PipelineRtEntityId] = config;
        }

        // Find pipelines to remove (registered but not in new config)
        var currentPipelineIds = GetRegisteredPipelines(tenantId).ToList();
        var toRemove = currentPipelineIds.Where(id => !newConfigsByPipelineId.ContainsKey(id)).ToList();

        // Find pipelines to add or update (new or changed)
        var toAddOrUpdate = new List<PipelineConfigurationDto>();
        foreach (var newConfig in pipelineConfigurations)
        {
            var key = CreateByIdKey(tenantId, newConfig.PipelineRtEntityId);
            if (_pipelineConfigurationsById.TryGetValue(key, out var existingConfig))
            {
                if (!existingConfig.Equals(newConfig))
                {
                    toAddOrUpdate.Add(newConfig);
                }
            }
            else
            {
                toAddOrUpdate.Add(newConfig);
            }
        }

        logger.LogInformation(
            "Selective pipeline update for tenant {TenantId}. Total: {Total}, Unchanged: {Unchanged}, Changed/New: {Changed}, Removed: {Removed}",
            tenantId, pipelineConfigurations.Count,
            pipelineConfigurations.Count - toAddOrUpdate.Count,
            toAddOrUpdate.Count, toRemove.Count);

        // Unregister removed pipelines
        foreach (var pipelineId in toRemove)
        {
            logger.LogInformation("Removing pipeline {PipelineRtEntityId} for tenant {TenantId}",
                pipelineId, tenantId);
            await UnregisterPipelineAsync(tenantId, pipelineId);
        }

        // Unregister changed pipelines before re-registering
        foreach (var config in toAddOrUpdate)
        {
            if (IsRegistered(tenantId, config.PipelineRtEntityId))
            {
                logger.LogInformation("Re-registering changed pipeline {PipelineRtEntityId} for tenant {TenantId}",
                    config.PipelineRtEntityId, tenantId);
                await UnregisterPipelineAsync(tenantId, config.PipelineRtEntityId);
            }
            else
            {
                logger.LogInformation("Registering new pipeline {PipelineRtEntityId} for tenant {TenantId}",
                    config.PipelineRtEntityId, tenantId);
            }
        }

        // Register new/changed pipelines
        var success = true;
        foreach (var pipelineConfiguration in toAddOrUpdate)
        {
            try
            {
                await RegisterPipelineAsync(tenantId, pipelineConfiguration);
            }
            catch (PipelineSerializationException e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.PipelineDeserializationError,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
            catch (PipelineTriggerExecutionException e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.PipelineTriggerExecutionError,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
            catch (PipelineExecutionException e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.PipelineInitializationError,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
            catch (Exception e)
            {
                deploymentErrorMessages.Add(new DeploymentUpdateErrorMessageDto
                {
                    ErrorCategory = DeploymentErrorCategories.Uncategorized,
                    PipelineRtEntityId = pipelineConfiguration.PipelineRtEntityId,
                    DataFlowRtId = pipelineConfiguration.DataFlowRtId,
                    ErrorMessage = e.GetDirectAndIndirectMessages()
                });
                success = false;
            }
        }

        return success;
    }

    /// <inheritdoc />
    public bool IsRegistered(string tenantId, RtEntityId pipelineRtEntityId)
    {
        return _pipelineRegistrationsById.ContainsKey(CreateByIdKey(tenantId, pipelineRtEntityId));
    }

    /// <inheritdoc />
#if !NETSTANDARD2_0
    public bool TryGetPipelineRegistration(string tenantId, RtEntityId pipelineRtEntityId,
        [NotNullWhen(true)] out PipelineRegistration? pipelineRegistration)
#else
    public bool TryGetPipelineRegistration(string tenantId, RtEntityId pipelineRtEntityId,
        out PipelineRegistration? pipelineRegistration)
#endif

    {
        return _pipelineRegistrationsById.TryGetValue(CreateByIdKey(tenantId, pipelineRtEntityId),
            out pipelineRegistration);
    }

    /// <inheritdoc />
    public IEnumerable<RtEntityId> GetRegisteredPipelines(string tenantId)
    {
        var normalizedTenantId = tenantId.NormalizeString();
        return _pipelineRegistrationsById
            .Where(kvp => kvp.Key.Item1 == normalizedTenantId)
            .Select(kvp => kvp.Key.Item2);
    }

    /// <summary>
    /// Create a key for the pipeline execution item
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="pipelineRtEntityId"></param>
    /// <returns></returns>
    // ReSharper disable once MemberCanBePrivate.Global
    private static Tuple<string, RtEntityId> CreateByIdKey(string tenantId, RtEntityId pipelineRtEntityId)
    {
        return new Tuple<string, RtEntityId>(tenantId.NormalizeString(), pipelineRtEntityId);
    }

    /// <summary>
    /// Create a key for the pipeline execution item by data flow id
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="dataFlowRtId"></param>
    /// <returns></returns>
    private static Tuple<string, OctoObjectId> CreateDataFlowIdKey(string tenantId, OctoObjectId dataFlowRtId)
    {
        return new Tuple<string, OctoObjectId>(tenantId.NormalizeString(), dataFlowRtId);
    }
}