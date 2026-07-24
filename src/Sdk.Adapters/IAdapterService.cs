using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Interface for the adapter service that allows to start and stop a adapter
/// </summary>
/// <remarks>
///     This interface needs to be implemented by the adapter assembly and registered in the DI container.
/// </remarks>
public interface IAdapterService
{
    /// <summary>
    ///     Gets called when the adapter service should start.
    /// </summary>
    /// <param name="adapterStartup">Startup configuration</param>
    /// <param name="errorMessages">A list of error messages that occurred during the startup</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation of the adapter</param>
    /// <returns></returns>
    Task<bool> StartupAsync(AdapterStartup adapterStartup, List<DeploymentUpdateErrorMessageDto> errorMessages,
        CancellationToken stoppingToken);

    /// <summary>
    ///     Gets called when the adapter service should stop.
    /// </summary>
    /// <param name="adapterShutdown">Shutdown configuration</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation of the adapter</param>
    /// <returns></returns>
    Task ShutdownAsync(AdapterShutdown adapterShutdown, CancellationToken stoppingToken);

    /// <summary>
    ///     Gets called when the adapter's configuration was updated at runtime
    ///     (e.g. via "Update Configuration" in Refinery Studio) without a full restart.
    /// </summary>
    /// <remarks>
    ///     Pipelines are updated selectively by the SDK independently of this call, so the adapter
    ///     must not re-register or tear down pipelines here. Override this only to apply the updated
    ///     adapter-level configuration (<see cref="AdapterConfigurationDto.AdapterConfiguration" />).
    ///     The default implementation is a no-op, so adapters without adapter-level configuration are
    ///     unaffected.
    /// </remarks>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="adapterConfiguration">The updated adapter configuration</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation of the adapter</param>
    /// <returns></returns>
    Task ConfigurationUpdatedAsync(string tenantId, AdapterConfigurationDto adapterConfiguration,
        CancellationToken stoppingToken) => Task.CompletedTask;

    /// <summary>
    ///     Gets called when the tenant's Construction Kit model may have changed (CK model import,
    ///     cache clear). Adapters that hold an in-process CK model cache must invalidate it here so
    ///     subsequent pipeline executions validate against the current model (AB#4456).
    /// </summary>
    /// <remarks>
    ///     Default implementation is a no-op so adapters without a CK cache are unaffected.
    ///     The adapter is not restarted; running pipelines keep their registrations.
    /// </remarks>
    /// <param name="tenantId">Tenant identifier</param>
    /// <returns></returns>
    Task CkModelChangedAsync(string tenantId)
    {
        return Task.CompletedTask;
    }
}