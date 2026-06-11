using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using NLog;

namespace Sdk.Socket.WebSample;

public class WebDemoAdapterService : IAdapterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Task<bool> StartupAsync(AdapterStartup adapterStartup, List<DeploymentUpdateErrorMessageDto> errorMessages,
        CancellationToken stoppingToken)
    {
        try
        {
            Logger.Info("WebDemoAdapterService started");
            return Task.FromResult(true);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error while startup");
            throw;
        }
    }

    public Task ShutdownAsync(AdapterShutdown adapterShutdown, CancellationToken stoppingToken)
    {
        try
        {
            Logger.Info("WebDemoAdapterService stopped");
            return Task.CompletedTask;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error while shutdown");
            throw;
        }
    }
}