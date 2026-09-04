
namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Base class of trigger extract node configuration
/// </summary>
public record TriggerNodeConfiguration : NodeConfiguration, ITriggerNodeConfiguration
{
    /// <summary>
    ///     How this trigger turns the <b>sender of the trigger</b> into the execution's verified
    ///     caller (AB#5126). The three-state policy — anonymous allowed / binding optional / binding
    ///     required — that makes running anonymous a deliberate choice rather than an accident of
    ///     whether a channel happened to resolve a caller.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         🟢 <b>Additive &amp; migration-safe.</b> The default, <see cref="CallerBindingMode.AnonymousAllowed" />
    ///         (value <c>0</c>), reproduces the pre-AB#5126 behaviour byte for byte: no directory lookup
    ///         is attempted and a caller that is already present on the execution is still honoured, so
    ///         a pipeline authored before this property existed keeps running unchanged.
    ///     </para>
    ///     <para>
    ///         Only triggers that carry a <b>sender identity</b> — the channel triggers (Teams, Signal,
    ///         e-mail) and <c>FromExecutePipelineCommand</c> — honour this property. Internal / event /
    ///         polling triggers (<c>FromPipelineDataEvent</c>, <c>FromWatchRtEntity</c>,
    ///         <c>FromPolling</c>, …) have no sender and ignore it. <c>FromHttpRequest@2</c> keeps its
    ///         own pre-existing bearer model (<c>AllowAnonymous</c> / <c>RequiredRoles</c>); its caller
    ///         is already carried onto the execution.
    ///     </para>
    /// </remarks>
    [PropertyGroup("Security", 90)]
    public CallerBindingMode CallerBinding { get; set; } = CallerBindingMode.AnonymousAllowed;
}