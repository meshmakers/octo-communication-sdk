namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

internal class EdgeDataBufferException : Exception
{
    public EdgeDataBufferException()
    {
    }

    public EdgeDataBufferException(string message) : base(message)
    {
    }

    public EdgeDataBufferException(string message, Exception inner) : base(message, inner)
    {
    }

    public static Exception MetadataUninitialized() =>
        new EdgeDataBufferException("EdgeDataBuffer is not initialized");

    public static Exception Disposed() => new EdgeDataBufferException("EdgeDataBuffer is disposed");

    public static Exception ChunkedBufferHasInvalidStateForOperation(ChunkedDataBufferState metadataState,
        ChunkedDataBufferState requiredState) =>
        new EdgeDataBufferException(
            $"Chunked buffer has invalid state {metadataState} for operation. Required state is {requiredState}");

    public static Exception CantDeleteFile(string fileName) =>
        new EdgeDataBufferException($"Cant delete database file '{fileName}'");

    public static Exception CantOpenDatabaseFile(string filePath) =>
        new EdgeDataBufferException($"Cant open database file '{filePath}'");

    public static Exception NoOpenChunkToClose() =>
        new EdgeDataBufferException("Can't close chunk. No open chunk found");
}