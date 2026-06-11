namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

internal struct OperationResult<T>
{
    public bool Success { get; private init; }
    public T? Result { get; private init; }
    
    public static OperationResult<T> Ok(T result) => new() {Success = true, Result = result};
    public static OperationResult<T> Error() => new() {Success = false};
}