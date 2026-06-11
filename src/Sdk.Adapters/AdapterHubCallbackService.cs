using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Implementation of the adapter hub callback service
/// </summary>
public class AdapterHubCallbackService : IAdapterHubCallbacks, IAdapterHubCallbackService
{
    private IAdapterHubCallbacks? _adapterHubCallbacks;

    /// <inheritdoc />
    public async Task AdapterConfigurationUpdatedAsync(string tenantId, AdapterConfigurationDto adapterConfiguration)
    {
        var callback = _adapterHubCallbacks;
        if (callback != null)
        {
            await callback.AdapterConfigurationUpdatedAsync(tenantId, adapterConfiguration);
        }
    }

    /// <inheritdoc />
    public async Task PreUpdateTenantAsync(string tenantId)
    {
        var callback = _adapterHubCallbacks;
        if (callback != null)
        {
            await callback.PreUpdateTenantAsync(tenantId);
        }
    }

    /// <inheritdoc />
    public void RegisterCallback(IAdapterHubCallbacks adapterHubCallbacks)
    {
        _adapterHubCallbacks = adapterHubCallbacks;
    }
}