using System.Text.Json.Nodes;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;

/// <summary>
///     Configuration for the distribution event hub node
/// </summary>
[NodeName("BufferData", 1)]
public record BufferNodeConfiguration : PathNodeConfiguration, IChildNodeConfiguration
{
    /// <summary>
    /// </summary>
    [PropertyGroup("Timing", 0)]
    public string? BufferTime { get; set; } = "00:05:00";

    /// <summary>
    ///     An optional flag, that if set to true, will keep the data in the buffer after sending it to the distribution event
    ///     hub
    /// </summary>
    [PropertyGroup("Options", 0)]
    public bool KeepDataAfterSending { get; set; }

    /// <inheritdoc />
    public ICollection<NodeConfiguration>? Transformations { get; set; }
}

/// <summary>
///     Publishes the target object to the distribution event hub
/// </summary>
[NodeConfiguration(typeof(BufferNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
internal class BufferNode(
    NodeDelegate next,
    IEdgeDataBuffer<Dictionary<string, BsonValue>> buffer,
    IEtlContext context,
    IEtlDataOrchestrator orchestrator) : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<BufferNodeConfiguration>();

        // we store the data in the buffer
        HandleLoad(dataContext, c);

        // we figure out if we need to reconfigure the buffer to send data
        if (!IsConfigUpToDate(c))
        {
            // we need to store the configuration in the data context, so we can figure out next run if it has changed.
            context.Properties[nameof(BufferNodeConfiguration)] = c;

            var scheduler = nodeContext.ServiceProvider.GetRequiredService<IBufferScheduler>();

            var timeSpan = TimeSpan.TryParse(c.BufferTime, out var ts) ? ts : TimeSpan.FromSeconds(10);

            scheduler.ScheduleOrReplace(async () =>
            {
                // the user does not have to specify the buffer retrieval node
                var updatedTransforms = new List<NodeConfiguration>
                {
                    new BufferRetrievalNodeConfiguration
                    {
                        KeepDataAfterSending = c.KeepDataAfterSending,
                    }
                };

                updatedTransforms.AddRange(c.Transformations ?? []);

                //this is the pipeline that loads the data and sends it to the buffer
                await orchestrator.ExecutePipelineAsync(
                    new NodeDefinitionRoot { Transformations = updatedTransforms },
                    context, nodeContext.PipelineDebugger, new JsonObject());
            }, timeSpan);
        }

        await next(dataContext, nodeContext);
    }

    private bool IsConfigUpToDate(BufferNodeConfiguration currentConfig)
    {
        if (!context.Properties.TryGetValue(nameof(BufferNodeConfiguration), out var c) ||
            c is not BufferNodeConfiguration config)
        {
            return false;
        }

        return JsonSerializer.Serialize(currentConfig, SystemTextJsonOptions.Default) ==
               JsonSerializer.Serialize(config, SystemTextJsonOptions.Default);
    }

    private void HandleLoad(IDataContext dataContext, BufferNodeConfiguration c)
    {
        var node = dataContext.Get<JsonNode>(c.Path);
        var data = LiteDbBsonConverter.ToDictionary(node);

        var chunk = buffer.GetOrCreateOpenChunk();
        chunk.AddDataPoint(DataPoint<Dictionary<string, BsonValue>>.CreateNew(data));

        // we have consumed the data create an empty data context for the next node;
        dataContext.Set("$", new JsonObject());
    }
}
