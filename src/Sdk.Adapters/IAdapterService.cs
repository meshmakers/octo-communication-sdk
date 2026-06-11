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
}