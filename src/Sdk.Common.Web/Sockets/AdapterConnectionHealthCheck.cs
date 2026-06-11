using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Meshmakers.Octo.Sdk.Common.Web.Sockets;

/// <summary>
/// Health check that reports the SignalR connection state of the adapter hub client.
/// </summary>
public class AdapterConnectionHealthCheck(IAdapterHubClient adapterHubClient) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (adapterHubClient.IsAlive)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Adapter is connected to communication hub."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Adapter is not connected to communication hub."));
    }
}
