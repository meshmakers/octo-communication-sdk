using System.Diagnostics;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Periodically samples the adapter process's CPU, memory and thread usage and
/// pushes each sample to the communication controller. The controller keeps a
/// short in-memory history per adapter to back the UI sparklines.
///
/// First sample of a session is consumed as a CPU baseline (no point published)
/// because CPU% is computed from the delta between two TotalProcessorTime
/// readings. Subsequent samples emit a 0..100% value normalised across all
/// available cores.
/// </summary>
public class AdapterMetricsSamplerService : BackgroundService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IAdapterHubClient _adapterHubClient;
    private readonly IOptions<AdapterOptions> _adapterOptions;

    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleAt;
    private bool _hasBaseline;

    /// <summary>
    /// Constructor
    /// </summary>
    public AdapterMetricsSamplerService(IAdapterHubClient adapterHubClient,
        IOptions<AdapterOptions> adapterOptions)
    {
        _adapterHubClient = adapterHubClient;
        _adapterOptions = adapterOptions;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _adapterOptions.Value;

        if (!options.MetricsSamplingEnabled)
        {
            Logger.Info("Adapter metrics sampler disabled via configuration.");
            return;
        }

        if (!TryResolveAdapterRtEntityId(options, out var adapterRtEntityId))
        {
            Logger.Warn("Adapter metrics sampler not started — AdapterRtId / AdapterCkTypeId not configured.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.MetricsSamplingIntervalSeconds));
        Logger.Info("Adapter metrics sampler started, interval {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_adapterHubClient.IsAlive)
                {
                    await SampleAndReportAsync(adapterRtEntityId);
                }
                else
                {
                    // Drop the baseline so the first sample after reconnect doesn't
                    // emit a misleading delta covering the offline gap.
                    _hasBaseline = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error capturing or reporting adapter metrics sample.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Logger.Info("Adapter metrics sampler stopped.");
    }

    private async Task SampleAndReportAsync(RtEntityId adapterRtEntityId)
    {
        using var process = Process.GetCurrentProcess();

        var now = DateTime.UtcNow;
        var totalCpuTime = process.TotalProcessorTime;

        if (!_hasBaseline)
        {
            _lastCpuTime = totalCpuTime;
            _lastSampleAt = now;
            _hasBaseline = true;
            return;
        }

        var wallMs = (now - _lastSampleAt).TotalMilliseconds;
        var cpuMs = (totalCpuTime - _lastCpuTime).TotalMilliseconds;
        _lastCpuTime = totalCpuTime;
        _lastSampleAt = now;

        var cores = Math.Max(1, Environment.ProcessorCount);
        var rawCpu = wallMs <= 0 ? 0.0 : cpuMs / (wallMs * cores) * 100.0;
        // Math.Clamp is not available on netstandard2.0; manual bound.
        var cpuPercent = Math.Min(100.0, Math.Max(0.0, rawCpu));

        var sample = new AdapterMetricsSampleDto
        {
            AdapterRtEntityId = adapterRtEntityId,
            Timestamp = now,
            CpuPercent = cpuPercent,
            WorkingSetBytes = process.WorkingSet64,
            GcHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            ThreadCount = process.Threads.Count
        };

        await _adapterHubClient.ReportAdapterMetricsAsync(sample);
    }

    private static bool TryResolveAdapterRtEntityId(AdapterOptions options, out RtEntityId adapterRtEntityId)
    {
        if (string.IsNullOrWhiteSpace(options.AdapterRtId) ||
            string.IsNullOrWhiteSpace(options.AdapterCkTypeId))
        {
            adapterRtEntityId = default;
            return false;
        }

        var rtId = OctoObjectId.Parse(options.AdapterRtId!);
        var ckId = new RtCkId<CkTypeId>(options.AdapterCkTypeId!);
        adapterRtEntityId = new RtEntityId(ckId, rtId);
        return true;
    }
}
