using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Represents the startup configuration for an adapter
/// </summary>
public record AdapterStartup
{
    /// <summary>
    ///     Returns the tenant id
    /// </summary>
    public string TenantId { get; init; } = null!;

    /// <summary>
    ///     Returns the received adapter configuration from the backend
    /// </summary>
    public AdapterConfigurationDto Configuration { get; init; } = null!;

    /// <summary>
    ///     Gets or sets whether to start the event hub (MassTransit bus) during startup.
    ///     Set to false for configuration updates where the bus is already running.
    /// </summary>
    public bool StartEventHub { get; init; } = true;
}