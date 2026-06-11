namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

internal static class Constants
{
    public const string DatabaseExtension = ".omedb";
    public const string MetaDataDatabaseName = "EdgeDataBufferMetadata" + DatabaseExtension;
    public const int RetrievalChunkSize = 1000;
    public const string DataCollectionName = "data";
    public static readonly string[] TimeStampKeys = ["timestamp", "Timestamp", "time", "Time", "ts", "TS", "Ts"];
}