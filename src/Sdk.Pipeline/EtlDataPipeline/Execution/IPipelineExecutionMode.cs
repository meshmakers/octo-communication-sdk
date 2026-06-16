namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

/// <summary>
/// Carries per-execution mode flags surfaced to pipeline nodes (e.g. dry-run).
/// Threaded onto the per-execution <see cref="Nodes.INodeContext"/> by
/// <see cref="IEtlDataOrchestrator.ExecutePipelineAsync{TEtlContext}"/> when the
/// caller passes a non-null instance; otherwise nodes see a null
/// <see cref="Nodes.INodeContext.PipelineExecutionMode"/> and behave as in any
/// classic (real-effect) run. M4-B.2 only defines the dry-run flag; further
/// mode flags can be added here without breaking the wire.
/// </summary>
public interface IPipelineExecutionMode
{
    /// <summary>
    /// When true, Load nodes that honour the flag MUST suppress their real
    /// side effect (no MongoDB write, no HTTP call, no SMTP/SFTP transmission)
    /// and instead emit a "would-have-written" payload via
    /// <see cref="Nodes.INodeContext.RecordDryRunIntent(string, object)"/>.
    /// Load nodes that do NOT honour the flag run for real; the executor
    /// reports them to the caller (via <c>LoadNodesNotHonouringDryRun</c> in
    /// the MCP <c>dry_run_pipeline</c> response) so the agent and the operator
    /// know which side effects might have fired.
    /// </summary>
    bool IsDryRun { get; }
}
