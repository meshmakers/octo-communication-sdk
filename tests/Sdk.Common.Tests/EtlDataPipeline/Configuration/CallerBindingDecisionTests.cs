using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration;

/// <summary>
///     Proves the pure three-state caller-binding decision (AB#5126): whether a trigger attempts a
///     directory lookup, and what it does with the result under each <see cref="CallerBindingMode" />.
///     This is the single rule every trigger — channel and ExecuteCommand alike — reaches through.
/// </summary>
public class CallerBindingDecisionTests
{
    [Fact]
    public void Default_mode_is_AnonymousAllowed_so_a_missing_value_reproduces_legacy_behaviour()
    {
        // A trigger config authored before AB#5126 deserialises its enum to 0. That value MUST be the
        // migration-safe one: no lookup, never a rejection.
        Assert.Equal(CallerBindingMode.AnonymousAllowed, default(CallerBindingMode));
        Assert.False(CallerBindingDecision.ShouldAttemptResolution(default));
    }

    [Theory]
    [InlineData(CallerBindingMode.AnonymousAllowed, false)]
    [InlineData(CallerBindingMode.BindingOptional, true)]
    [InlineData(CallerBindingMode.BindingRequired, true)]
    public void ShouldAttemptResolution_only_when_binding_is_wanted(CallerBindingMode mode, bool expected)
    {
        // AnonymousAllowed deliberately never looks — anonymous is a choice, not a failed lookup,
        // and the pre-AB#5126 default therefore costs no directory round trip.
        Assert.Equal(expected, CallerBindingDecision.ShouldAttemptResolution(mode));
    }

    [Theory]
    [InlineData(CallerBindingMode.AnonymousAllowed)]
    [InlineData(CallerBindingMode.BindingOptional)]
    [InlineData(CallerBindingMode.BindingRequired)]
    public void A_resolved_caller_is_always_used_regardless_of_mode(CallerBindingMode mode)
    {
        // Even AnonymousAllowed uses a caller that is already present (bearer / carried-through
        // invoker) — it just never went looking for one.
        Assert.Equal(CallerBindingOutcome.UseResolvedCaller, CallerBindingDecision.Decide(mode, callerResolved: true));
    }

    [Fact]
    public void AnonymousAllowed_without_a_caller_runs_as_the_service_account()
    {
        Assert.Equal(CallerBindingOutcome.RunAsServiceAccount,
            CallerBindingDecision.Decide(CallerBindingMode.AnonymousAllowed, callerResolved: false));
    }

    [Fact]
    public void BindingOptional_without_a_caller_falls_back_to_the_service_account()
    {
        Assert.Equal(CallerBindingOutcome.RunAsServiceAccount,
            CallerBindingDecision.Decide(CallerBindingMode.BindingOptional, callerResolved: false));
    }

    [Fact]
    public void BindingRequired_without_a_caller_is_rejected_never_downgraded()
    {
        // The load-bearing rule: a required binding that cannot be satisfied REFUSES — it must never
        // silently run as the service account.
        Assert.Equal(CallerBindingOutcome.Reject,
            CallerBindingDecision.Decide(CallerBindingMode.BindingRequired, callerResolved: false));
    }
}
