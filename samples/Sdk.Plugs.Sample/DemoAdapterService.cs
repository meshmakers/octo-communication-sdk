using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Adapters;
using NLog;

namespace Sdk.Plugs.Sample;

public class DemoAdapterService : IAdapterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Task<bool> StartupAsync(AdapterStartup adapterStartup, List<DeploymentUpdateErrorMessageDto> errorMessages,
        CancellationToken stoppingToken)
    {
        try
        {
            Logger.Info("DemoAdapterService started");
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
            Logger.Info("DemoAdapterService stopped");
            return Task.CompletedTask;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error while shutdown");
            throw;
        }
    }
}