using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Hosting;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Background service that writes a health file when the adapter is connected to the communication hub.
/// This enables Kubernetes exec-based liveness/readiness probes for non-web adapters.
/// </summary>
public class AdapterHealthFileService(IAdapterHubClient adapterHubClient) : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// The path to the health file. Kubernetes exec probes check for this file's existence.
    /// </summary>
    public const string HealthFilePath = "/tmp/adapter-healthy";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.Info("Adapter health file service started, writing to {HealthFilePath}", HealthFilePath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (adapterHubClient.IsAlive)
                {
                    await WriteHealthFileAsync(stoppingToken);
                }
                else
                {
                    RemoveHealthFile();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error updating health file");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Clean up on shutdown
        RemoveHealthFile();
        Logger.Info("Adapter health file service stopped");
    }

    private static async Task WriteHealthFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(HealthFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

#if NETSTANDARD2_0
            File.WriteAllText(HealthFilePath, DateTime.UtcNow.ToString("O"));
#else
            await File.WriteAllTextAsync(HealthFilePath, DateTime.UtcNow.ToString("O"), cancellationToken);
#endif
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to write health file at {HealthFilePath}", HealthFilePath);
        }
    }

    private static void RemoveHealthFile()
    {
        try
        {
            if (File.Exists(HealthFilePath))
            {
                File.Delete(HealthFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to remove health file at {HealthFilePath}", HealthFilePath);
        }
    }
}
