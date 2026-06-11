namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

/// <summary>
///     Represents the metadata of a chunk (one lite database file)
/// </summary>
internal class ChunkMetadata
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public required string FileName { get; set; }
    public ChunkedDataBufferState State { get; set; }
    public int DataCount { get; set; }
}