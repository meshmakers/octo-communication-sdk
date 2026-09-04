namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
///     What a trigger must do with an execution once it knows its <see cref="CallerBindingMode" />
///     and whether the sender resolved to a caller (AB#5126). The outcome of
///     <see cref="CallerBindingDecision.Decide" />.
/// </summary>
public enum CallerBindingOutcome
{
    /// <summary>Run the pipeline as the resolved caller (a binding existed and was accepted).</summary>
    UseResolvedCaller,

    /// <summary>
    ///     Run the pipeline with no caller — as the pipeline's service account, or the system context
    ///     when none is configured. This is the anonymous / fall-back path, never a rejection.
    /// </summary>
    RunAsServiceAccount,

    /// <summary>
    ///     Refuse the execution: the mode was <see cref="CallerBindingMode.BindingRequired" /> and the
    ///     sender did not resolve. The pipeline must NOT run and must NOT silently downgrade to the
    ///     service account.
    /// </summary>
    Reject
}

/// <summary>
///     The pure, dependency-free decision at the heart of the three-state caller-binding policy
///     (AB#5126). Kept as a static function precisely so it can be unit-tested exhaustively without a
///     directory, a repository or a channel, and so every trigger reaches the same verdict.
/// </summary>
public static class CallerBindingDecision
{
    /// <summary>
    ///     Whether the trigger should even <b>attempt</b> to resolve the sender against the directory.
    ///     <see cref="CallerBindingMode.AnonymousAllowed" /> deliberately skips the (potentially
    ///     costly) lookup — anonymous is a choice, not a failed resolution — while both binding modes
    ///     attempt it.
    /// </summary>
    public static bool ShouldAttemptResolution(CallerBindingMode mode)
        => mode != CallerBindingMode.AnonymousAllowed;

    /// <summary>
    ///     Maps <paramref name="mode" /> and whether a caller was resolved onto the action the trigger
    ///     must take.
    /// </summary>
    /// <param name="mode">The trigger's configured binding mode.</param>
    /// <param name="callerResolved">
    ///     <c>true</c> when a caller principal is available for this execution — either resolved from
    ///     the directory, or already carried onto the execution (bearer / carried-through invoker).
    /// </param>
    public static CallerBindingOutcome Decide(CallerBindingMode mode, bool callerResolved)
    {
        if (callerResolved)
        {
            // A caller is a caller regardless of the mode — even AnonymousAllowed uses one that is
            // already present (it just never went looking). This is what keeps a bearer-authenticated
            // HTTP route and a carried-through ExecuteCommand invoker acting as themselves.
            return CallerBindingOutcome.UseResolvedCaller;
        }

        // No caller resolved. Only BindingRequired turns that into a hard failure; the other two run
        // on as the service account. This is the single place the "never silently run as the service
        // account when binding was required" rule lives.
        return mode == CallerBindingMode.BindingRequired
            ? CallerBindingOutcome.Reject
            : CallerBindingOutcome.RunAsServiceAccount;
    }
}
