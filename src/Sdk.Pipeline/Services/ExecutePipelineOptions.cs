using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Options for executing a pipeline
/// </summary>
public class ExecutePipelineOptions(DateTime transactionStartedDateTime)
{
    /// <summary>
    /// Gets or sets the date and time when the transaction started
    /// </summary>
    public DateTime TransactionStartedDateTime { get; } = transactionStartedDateTime;

    /// <summary>
    /// Gets or sets the date and time when the transaction started
    /// </summary>
    public DateTime? ExternalReceivedDateTime { get; set; }

    /// <summary>
    /// Gets or sets the trigger type that initiated this execution
    /// </summary>
    public PipelineTriggerType TriggerType { get; set; } = PipelineTriggerType.Event;

    /// <summary>
    /// Gets or sets optional input data for debugging (will be truncated if too long)
    /// </summary>
    public string? InputData { get; set; }

    /// <summary>
    /// When true, the orchestrator runs the pipeline with
    /// <c>IPipelineExecutionMode.IsDryRun=true</c> so retrofitted Load nodes
    /// suppress their real sink and record a "would-have-written" intent on
    /// the debug stream (M4-B.2). Default false preserves classic semantics.
    /// </summary>
    public bool IsDryRun { get; set; }

    /// <summary>
    /// Gets or sets the authenticated caller of the trigger, verified by the trigger's own
    /// authorization (AB#4975). Null for anonymous and internal triggers.
    /// </summary>
    public VerifiedPrincipal? VerifiedPrincipal { get; set; }

    /// <summary>
    /// Gets or sets the <b>raw access token</b> the caller presented to the trigger, for nodes that
    /// have to act as the caller against another service — the delegation ("on-behalf-of") grant
    /// needs it as <c>subject_token</c> (AB#5026 / AB#5031). Null for anonymous and internal
    /// triggers, and for triggers that do not carry a credential.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Deliberately NOT on <see cref="VerifiedPrincipal" />.</b> That record is a slim,
    ///         token-free value object precisely because the trigger writes a projection of it into
    ///         the pipeline data root, which is echoed back in the HTTP response, persistable by
    ///         <c>SetPipelineExecutionResult@1</c> and visible in the Studio debug panel. Putting a
    ///         bearer token on it would leak the caller's credential into every one of those places.
    ///     </para>
    ///     <para>
    ///         <b>Deliberately NOT in the data context either</b>, for the same reason, and
    ///         deliberately not in <c>IEtlContext.Properties</c>: that dictionary is the pipeline
    ///         registration's own dictionary and is therefore shared across <b>all runs</b> of the
    ///         pipeline — one user's token would still be sitting there for the next user's request.
    ///         This property is a per-execution side channel: it travels from the trigger to the ETL
    ///         context of exactly this execution and nowhere else.
    ///     </para>
    /// </remarks>
    public string? CallerAccessToken { get; set; }
}