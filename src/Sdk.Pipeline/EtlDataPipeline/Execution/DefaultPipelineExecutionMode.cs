namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

/// <summary>
/// Default <see cref="IPipelineExecutionMode"/> POCO. Use when the caller of
/// <see cref="IEtlDataOrchestrator.ExecutePipelineAsync{TEtlContext}"/> needs
/// to set explicit mode flags for a single run without writing its own
/// implementation.
/// </summary>
public sealed class DefaultPipelineExecutionMode : IPipelineExecutionMode
{
    /// <inheritdoc />
    public bool IsDryRun { get; init; }
}
