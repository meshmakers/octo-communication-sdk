namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The <b>effective trust</b> of a verified caller as it travels through the pipeline, so a node
///     can demand a minimum ("delegate only when the caller is at least strongly authenticated" —
///     AB#5126). A channel-neutral mirror of the identity directory's trust scale (AB#5122,
///     <c>RtTrustLevelEnum</c>): the SDK cannot reference the generated identity CK model, and the
///     value that reaches a node is already the <b>effective</b> trust — <c>min(enrollment, message)</c>
///     — computed at the directory boundary, so no dimension detail is lost by flattening it here.
/// </summary>
/// <remarks>
///     A small, totally ordered scale — <c>None &lt; Weak &lt; Strong</c> — whose numeric keys match
///     <c>RtTrustLevelEnum</c> one-for-one so the future directory wiring (AB#5122/5123/5124/5125)
///     maps across the service boundary by value. The default is <see cref="None" /> so an execution
///     with no verified caller (anonymous / service account) never appears to meet a trust minimum.
/// </remarks>
public enum CallerTrustLevel
{
    /// <summary>No trust — anonymous, or a caller that carries no verified-identifier binding.</summary>
    None = 0,

    /// <summary>Weakly trusted — e.g. a self-asserted or single-factor binding.</summary>
    Weak = 1,

    /// <summary>Strongly trusted — e.g. a fully enrolled binding with an authenticated message.</summary>
    Strong = 2
}

/// <summary>
///     Helpers over the <see cref="CallerTrustLevel" /> scale.
/// </summary>
public static class CallerTrustLevels
{
    /// <summary>
    ///     Whether <paramref name="actual" /> meets the <paramref name="required" /> minimum — the
    ///     comparison a node makes when it wants to act only for a sufficiently trusted caller.
    /// </summary>
    public static bool IsAtLeast(this CallerTrustLevel actual, CallerTrustLevel required)
        => (int)actual >= (int)required;
}
