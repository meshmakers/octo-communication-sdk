namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
///     The identity a data node runs as (AB#5127, part of the pipeline-identity epic AB#4979). A
///     data node opens its repository session under exactly one of these — the choice is written
///     down in the node configuration instead of being an accident of which session method the call
///     site happens to use.
/// </summary>
/// <remarks>
///     A <b>missing</b> value deserialises to <see cref="Caller" /> (value <c>0</c>), so every
///     pipeline authored before this property existed keeps its previous behaviour byte for byte.
/// </remarks>
public enum NodeExecutionIdentity
{
    /// <summary>
    ///     Run as the execution's <b>effective caller</b>: the trigger-verified user when present,
    ///     otherwise the pipeline's service account, otherwise the system context (AB#5028). Reads and
    ///     writes are subject to the caller's data permissions (the intersection with the service
    ///     account's, AB#4969) and are stamped with the caller as creator. This is the default and
    ///     equals the behaviour that existed before AB#5127.
    /// </summary>
    Caller = 0,

    /// <summary>
    ///     Run as the pipeline's <b>effective service account</b> with its <b>full assigned roles</b>,
    ///     even when a caller principal is present — no user, no role intersection. This is the
    ///     elevation: a pipeline is invoke-gated as the user but executes this node as the service
    ///     account. Least privilege is achieved by linking a dedicated override service account (the
    ///     <c>Uses</c> edge, AB#5107/5108) carrying exactly the needed roles, not by reducing the role
    ///     semantics here.
    /// </summary>
    ServiceAccount = 1,

    /// <summary>
    ///     Run as the <b>system context</b>: unfiltered, bypasses all data-level permissions
    ///     (AB#4969) and stamps no creator. Previously only Import/Restore/Deploy nodes chose this in
    ///     code; this makes it an opt-in <i>value</i> for a caller-scoped data node.
    /// </summary>
    System = 2
}
