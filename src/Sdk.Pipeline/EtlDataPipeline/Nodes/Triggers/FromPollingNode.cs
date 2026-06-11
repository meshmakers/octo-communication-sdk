using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;

/// <summary>
/// Configuration for polling extract node
/// </summary>
[NodeName("FromPolling", 1)]
// ReSharper disable once ClassNeverInstantiated.Global
public record FromPollingNodeConfiguration : TriggerNodeConfiguration
{
    /// <summary>
    /// Defines the interval between each extraction
    /// </summary>
    [PropertyGroup("Timing", 0)]
    public required TimeSpan Interval { get; init; }

    /// <summary>
    /// Defines the input data
    /// </summary>
    [PropertyGroup("Data", 0)]
    public JsonNode? Input { get; init; }
}

/// <summary>
/// Triggers the extraction of data using a polling mechanism
/// </summary>
[NodeConfiguration(typeof(FromPollingNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class FromPollingNode(IPollingService pollingService) : ITriggerPipelineNode
{
    private PollingHandle? _pollingHandle;

    /// <inheritdoc />
    public Task StartAsync(ITriggerContext triggerContext)
    {
        var c = triggerContext.NodeContext.GetNodeConfiguration<FromPollingNodeConfiguration>();
        _pollingHandle = pollingService.RegisterCallback(c.Interval, async () =>
        {
            var executePipelineOptions = new ExecutePipelineOptions(DateTime.UtcNow);
            await triggerContext.ExecuteAsync(executePipelineOptions, c.Input);
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(ITriggerContext triggerContext)
    {
        _pollingHandle?.Dispose();

        return Task.CompletedTask;
    }
}
