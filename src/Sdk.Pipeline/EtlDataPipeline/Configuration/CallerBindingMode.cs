namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
///     Per-trigger policy for turning the <b>sender of a trigger</b> into the execution's verified
///     caller (AB#5126, "Strang B" of the pipeline-identity epic AB#4979). It makes "run anonymous"
///     a <b>deliberate</b> choice instead of an accident of whether a channel happened to resolve a
///     caller: a trigger says up front whether an unresolved sender is acceptable, tolerated, or a
///     hard error.
/// </summary>
/// <remarks>
///     <para>
///         The three states are a small, totally ordered scale of <b>increasing strictness</b> —
///         <c>AnonymousAllowed &lt; BindingOptional &lt; BindingRequired</c> — mirroring the
///         <c>None &lt; Weak &lt; Strong</c> shape of the trust scale (AB#5122). The numeric keys carry
///         that order.
///     </para>
///     <para>
///         🟢 <b>Migration-safe by construction.</b> A <b>missing</b> value deserialises to
///         <see cref="AnonymousAllowed" /> (value <c>0</c>) — exactly the same pattern
///         <see cref="NodeExecutionIdentity" /> uses. And <see cref="AnonymousAllowed" /> reproduces
///         today's behaviour byte for byte: no directory lookup is attempted, so a channel trigger
///         runs as the service account (or system context) exactly as it did before this WI, and a
///         caller that is <i>already</i> present on the execution (e.g. the bearer of
///         <c>FromHttpRequest@2</c>, or the carried-through invoker of
///         <c>FromExecutePipelineCommand</c>) is still honoured. Every pipeline authored before this
///         property existed therefore keeps running unchanged.
///     </para>
/// </remarks>
public enum CallerBindingMode
{
    /// <summary>
    ///     Anonymous is a <b>first-class choice</b>: the trigger does <b>not</b> attempt to resolve
    ///     the sender against the verified-identifier directory. A caller already present on the
    ///     execution is still used; otherwise the execution runs as the service account / system
    ///     context. This is the default and equals the behaviour that existed before AB#5126.
    /// </summary>
    AnonymousAllowed = 0,

    /// <summary>
    ///     Bind <b>when possible</b>: the trigger resolves the sender against the directory and runs
    ///     as the resolved caller when a binding exists; when the sender cannot be resolved it falls
    ///     back to the service account (never rejected). The channel equivalent of an authenticated
    ///     route that also accepts anonymous callers.
    /// </summary>
    BindingOptional = 1,

    /// <summary>
    ///     Bind <b>or reject</b>: an unresolved sender is refused and the pipeline does <b>not</b>
    ///     run — deliberately never silently downgraded to the service account. The strict mode for
    ///     a channel that must act only on behalf of an identified user.
    /// </summary>
    BindingRequired = 2
}
