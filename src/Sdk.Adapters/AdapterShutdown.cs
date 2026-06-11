namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Represents the shutdown configuration for an adapter
/// </summary>
public record AdapterShutdown
{
    /// <summary>
    ///     Returns the tenant id
    /// </summary>
    public string TenantId { get; init; } = null!;

    /// <summary>
    ///     Gets or sets whether to stop the event hub (MassTransit bus) during shutdown.
    ///     Set to false for configuration updates where only pipeline re-registration is needed.
    /// </summary>
    public bool StopEventHub { get; init; } = true;
}