using System.Text.Json.Nodes;
using LiteDB;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;

/// <summary>
///     Dummy configuration for the buffer retrieval node.
///     This is only required for the infrastructure to work.
///     Should never be set in a real pipeline configuration.
/// </summary>
[NodeName("BufferRetrievalNode", 1)]
internal record BufferRetrievalNodeConfiguration : NodeConfiguration
{
    /// <summary>
    ///     Gets or sets a value indicating whether the data should be kept after sending.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public bool KeepDataAfterSending { get; set; }
}

[NodeConfiguration(typeof(BufferRetrievalNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
internal class BufferRetrievalNode(NodeDelegate next, IEdgeDataBuffer<Dictionary<string, BsonValue>> buffer)
    : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        buffer.TryCloseCurrentChunk(true);

        var c = nodeContext.GetNodeConfiguration<BufferRetrievalNodeConfiguration>();

        // Loop through each closed chunk
        foreach (var closedChunk in buffer.GetClosedChunks().ToList())
        {
            try
            {
                // Break large results into smaller pieces
                foreach (var chunk in Chunks(closedChunk))
                {
                    // Instead of merging the dictionaries, keep them as an array
                    var arrayOfObjects = new JsonArray();

                    foreach (var dictionaryItem in chunk)
                    {
                        var jsonObject = new JsonObject();
                        foreach (var kvp in dictionaryItem)
                        {
                            // Convert each BsonValue to a JsonNode without merging
                            jsonObject[kvp.Key] = LiteDbBsonConverter.FromBson(kvp.Value);
                        }

                        arrayOfObjects.Add(jsonObject);
                    }

                    // Place this array of objects into the data context
                    // (You can store it at any path you want, below is just an example)
                    dataContext.Set("$", arrayOfObjects, DocumentModes.Extend, ValueKinds.Array,
                        TargetValueWriteModes.Overwrite);

                    // Continue processing
                    await next(dataContext, nodeContext);
                }

                // Mark the chunk as sent
                buffer.MarkAsSent(closedChunk);

                // Optionally delete it if we do not keep data
                if (!c.KeepDataAfterSending)
                {
                    buffer.DeleteChunk(closedChunk);
                }
            }
            catch (Exception ex)
            {
                nodeContext.Error($"Error processing closed chunk: {ex.Message}");
            }
            finally
            {
                closedChunk.Dispose();
            }
        }
    }

    private static IEnumerable<Dictionary<string, BsonValue>[]> Chunks(
        IDisposableChunkedDataBuffer<Dictionary<string, BsonValue>> closedChunk)
    {
        return closedChunk.GetDataPoints().Select(x => x.Data)
            .Chunk(Constants.RetrievalChunkSize);
    }
}
