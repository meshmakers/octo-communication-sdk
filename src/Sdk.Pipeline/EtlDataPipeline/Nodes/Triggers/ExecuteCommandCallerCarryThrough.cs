using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;

/// <summary>
///     Carries the invoker of a manual <see cref="ExecutePipelineRequest" /> through onto the
///     execution options (AB#5126). <c>FromExecutePipelineCommand</c> is manually invoked and the
///     invoker already holds a token, so — unlike the channel triggers — there is nothing to resolve
///     against the directory: the caller is simply mapped off the message and the three-state
///     <see cref="CallerBindingMode" /> is enforced over "was an invoker carried?".
/// </summary>
/// <remarks>
///     Kept as a pure static so the mapping and the three-state enforcement can be unit-tested
///     without a bus, a context, or a running pipeline.
/// </remarks>
public static class ExecuteCommandCallerCarryThrough
{
    /// <summary>
    ///     The token-free principal projection of the carried invoker, or <c>null</c> when the message
    ///     carried none (an internal invocation, or a pre-AB#5126 controller).
    /// </summary>
    public static VerifiedPrincipal? ToPrincipal(ExecutePipelineCaller? caller)
        => caller == null
            ? null
            : new VerifiedPrincipal(caller.SubjectId, caller.TenantId, caller.Email, caller.Name, caller.Roles);

    /// <summary>
    ///     The invoker's effective trust as a <see cref="CallerTrustLevel" />, clamped to the defined
    ///     scale. <see cref="CallerTrustLevel.None" /> when no invoker was carried.
    /// </summary>
    public static CallerTrustLevel ToTrust(ExecutePipelineCaller? caller)
    {
        if (caller == null)
        {
            return CallerTrustLevel.None;
        }

        return caller.TrustLevel switch
        {
            >= (int)CallerTrustLevel.Strong => CallerTrustLevel.Strong,
            (int)CallerTrustLevel.Weak => CallerTrustLevel.Weak,
            _ => CallerTrustLevel.None
        };
    }

    /// <summary>
    ///     Applies the carry-through to <paramref name="options" /> under the trigger's
    ///     <paramref name="mode" /> and returns the outcome. On
    ///     <see cref="CallerBindingOutcome.UseResolvedCaller" /> the invoker principal, its raw token
    ///     (for delegation) and its trust are written onto the options; on
    ///     <see cref="CallerBindingOutcome.RunAsServiceAccount" /> nothing is written (anonymous run);
    ///     on <see cref="CallerBindingOutcome.Reject" /> nothing is written and the pipeline must not
    ///     run.
    /// </summary>
    public static CallerBindingOutcome Apply(ExecutePipelineRequest message, CallerBindingMode mode,
        ExecutePipelineOptions options)
    {
        var principal = ToPrincipal(message.Caller);
        var outcome = CallerBindingDecision.Decide(mode, principal != null);

        if (outcome == CallerBindingOutcome.UseResolvedCaller)
        {
            options.VerifiedPrincipal = principal;
            options.CallerAccessToken = message.CallerAccessToken;
            options.CallerTrust = ToTrust(message.Caller);
        }

        return outcome;
    }
}
