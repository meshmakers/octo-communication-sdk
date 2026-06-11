using LiteDB;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

/// <summary>
///     One datapoint in the buffer
/// </summary>
internal class DataPoint<T>
{
    [BsonId(true)] public int Id { get; set; }
    
    public required T Data { get; set; }

    public DateTimeOffset BufferedAt { get; set; }


    internal static DataPoint<T> CreateNew(T data)
    {
        return new()
        {
            Data = data,
            BufferedAt = DateTimeOffset.UtcNow
        };
    }
}