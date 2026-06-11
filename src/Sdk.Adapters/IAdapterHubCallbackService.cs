using Meshmakers.Octo.Communication.Contracts.Hubs;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Interface of adapter hub callback service
/// </summary>
public interface IAdapterHubCallbackService
{
    /// <summary>
    /// Register a callback
    /// </summary>
    /// <param name="adapterHubCallbacks"></param>
    void RegisterCallback(IAdapterHubCallbacks adapterHubCallbacks);
}