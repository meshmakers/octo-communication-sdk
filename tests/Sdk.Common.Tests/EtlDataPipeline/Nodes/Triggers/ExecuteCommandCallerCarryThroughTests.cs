using Meshmakers.Octo.Communication.Contracts.MessageObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Triggers;

/// <summary>
///     Proves the ExecuteCommand carry-through (AB#5126): a manual execution maps its carried invoker
///     onto the execution options as the caller, honours the three-state binding policy, and never
///     leaks the invoker's token into the token-free principal.
/// </summary>
public class ExecuteCommandCallerCarryThroughTests
{
    private static ExecutePipelineCaller Invoker() => new()
    {
        SubjectId = "user-42",
        TenantId = "acme",
        Email = "u@example.com",
        Name = "User Forty-Two",
        Roles = ["Reader", "Accounting"],
        TrustLevel = 2
    };

    private static ExecutePipelineRequest RequestWithInvoker() =>
        new("acme", "{}") { Caller = Invoker(), CallerAccessToken = "raw-token" };

    private static ExecutePipelineRequest AnonymousRequest() => new("acme", "{}");

    [Fact]
    public void ToPrincipal_maps_every_claim_and_stays_token_free()
    {
        var principal = ExecuteCommandCallerCarryThrough.ToPrincipal(Invoker());

        Assert.NotNull(principal);
        Assert.Equal("user-42", principal!.SubjectId);
        Assert.Equal("acme", principal.TenantId);
        Assert.Equal("u@example.com", principal.Email);
        Assert.Equal("User Forty-Two", principal.Name);
        Assert.Equal(new[] { "Reader", "Accounting" }, principal.Roles);
        // VerifiedPrincipal carries no token field at all — the credential can only travel on the
        // separate options.CallerAccessToken. (Compile-time guarantee; asserted here for intent.)
    }

    [Fact]
    public void ToPrincipal_of_a_missing_invoker_is_null()
    {
        Assert.Null(ExecuteCommandCallerCarryThrough.ToPrincipal(null));
    }

    [Theory]
    [InlineData(0, CallerTrustLevel.None)]
    [InlineData(1, CallerTrustLevel.Weak)]
    [InlineData(2, CallerTrustLevel.Strong)]
    [InlineData(99, CallerTrustLevel.Strong)] // clamped to the top of the defined scale
    public void ToTrust_clamps_the_wire_value_onto_the_scale(int wire, CallerTrustLevel expected)
    {
        Assert.Equal(expected, ExecuteCommandCallerCarryThrough.ToTrust(new ExecutePipelineCaller { TrustLevel = wire }));
    }

    [Fact]
    public void ToTrust_of_a_missing_invoker_is_None()
    {
        Assert.Equal(CallerTrustLevel.None, ExecuteCommandCallerCarryThrough.ToTrust(null));
    }

    [Fact]
    public void Apply_carries_the_invoker_principal_token_and_trust_onto_the_options()
    {
        var options = new ExecutePipelineOptions(DateTime.UtcNow);

        var outcome = ExecuteCommandCallerCarryThrough.Apply(RequestWithInvoker(),
            CallerBindingMode.AnonymousAllowed, options);

        Assert.Equal(CallerBindingOutcome.UseResolvedCaller, outcome);
        Assert.Equal("user-42", options.VerifiedPrincipal!.SubjectId);
        Assert.Equal("raw-token", options.CallerAccessToken);
        Assert.Equal(CallerTrustLevel.Strong, options.CallerTrust);
    }

    [Fact]
    public void Apply_without_an_invoker_runs_anonymously_and_writes_nothing()
    {
        var options = new ExecutePipelineOptions(DateTime.UtcNow);

        var outcome = ExecuteCommandCallerCarryThrough.Apply(AnonymousRequest(),
            CallerBindingMode.BindingOptional, options);

        Assert.Equal(CallerBindingOutcome.RunAsServiceAccount, outcome);
        Assert.Null(options.VerifiedPrincipal);
        Assert.Null(options.CallerAccessToken);
        Assert.Equal(CallerTrustLevel.None, options.CallerTrust);
    }

    [Fact]
    public void Apply_rejects_a_required_binding_when_no_invoker_was_carried()
    {
        var options = new ExecutePipelineOptions(DateTime.UtcNow);

        var outcome = ExecuteCommandCallerCarryThrough.Apply(AnonymousRequest(),
            CallerBindingMode.BindingRequired, options);

        Assert.Equal(CallerBindingOutcome.Reject, outcome);
        // Nothing written — the pipeline must not run, and must not run as the service account.
        Assert.Null(options.VerifiedPrincipal);
        Assert.Null(options.CallerAccessToken);
    }

    [Fact]
    public void Apply_uses_the_carried_invoker_even_under_required_binding()
    {
        var options = new ExecutePipelineOptions(DateTime.UtcNow);

        var outcome = ExecuteCommandCallerCarryThrough.Apply(RequestWithInvoker(),
            CallerBindingMode.BindingRequired, options);

        Assert.Equal(CallerBindingOutcome.UseResolvedCaller, outcome);
        Assert.Equal("user-42", options.VerifiedPrincipal!.SubjectId);
    }
}
