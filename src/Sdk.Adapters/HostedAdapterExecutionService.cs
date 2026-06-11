using Microsoft.Extensions.Hosting;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// The hosted service for the execution of an adapter.
/// Implements retry logic with exponential backoff to handle transient connection failures.
/// </summary>
/// <param name="adapterExecutionService"></param>
public class HostedAdapterExecutionService(AdapterExecutionService adapterExecutionService) : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = InitialRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await adapterExecutionService.StartAsync(stoppingToken);
                // Startup succeeded, reset retry delay and wait until cancellation
                retryDelay = InitialRetryDelay;
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Adapter startup failed, retrying in {RetryDelay}", retryDelay);

                // Clean up before retry
                try
                {
                    await adapterExecutionService.StopAsync(CancellationToken.None);
                }
                catch (Exception stopEx)
                {
                    Logger.Warn(stopEx, "Error during cleanup before retry");
                }

                try
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                retryDelay = TimeSpan.FromMilliseconds(
                    Math.Min(retryDelay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await adapterExecutionService.StopAsync(cancellationToken);
    }
}
