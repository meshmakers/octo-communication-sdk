using Meshmakers.Octo.Common.DistributionEventHub.Services;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.Services;
using NLog;

namespace Sdk.Plug.Simulation;

public class SimulationAdapterService(
    IPipelineRegistryService pipelineRegistryService,
    IEventHubControl eventHubControl)
    : IAdapterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public async Task<bool> StartupAsync(AdapterStartup adapterStartup,
        List<DeploymentUpdateErrorMessageDto> errorMessages,
        CancellationToken stoppingToken)
    {
        try
        {
            Logger.Info("SimulationPlugService started");

            var success = await pipelineRegistryService.RegisterPipelinesAsync(adapterStartup.TenantId,
                adapterStartup.Configuration.Pipelines, errorMessages);

            await eventHubControl.StartAsync(stoppingToken);

            return success;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error while startup");
            throw;
        }
    }

    public async Task ShutdownAsync(AdapterShutdown adapterShutdown, CancellationToken stoppingToken)
    {
        try
        {
            Logger.Info("SimulationPlugService stopping");

            // Unregister pipelines first to stop trigger nodes before stopping the bus.
            // Trigger nodes fire on timers and send MassTransit messages.
            // If the bus is stopped first, running triggers keep sending new messages,
            // preventing the bus from stopping.
            await pipelineRegistryService.UnregisterAllPipelinesAsync(adapterShutdown.TenantId);
            await eventHubControl.StopAsync(stoppingToken);

            Logger.Info("SimulationPlugService stopped");
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error while shutdown");
            throw;
        }
    }
}