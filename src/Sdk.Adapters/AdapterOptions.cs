namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Represents the adapter options
/// </summary>
public class AdapterOptions
{
    /// <summary>
    ///     Constructor
    /// </summary>
    public AdapterOptions()
    {
        TenantId = "meshTest";
        CommunicationControllerServicesUri = "https://localhost:5015";
        BrokerHost = "localhost";
        BrokerVirtualHost = "/";
        BrokerPort = 5672;
        BrokerUsername = "guest";
        BrokerPassword = "guest";
        AdapterCkTypeId = "System.Communication/Adapter";
        NlogConfigPath = "nlog.config";
    }

    /// <summary>
    ///     Gets or sets the prefix for the OctoMesh installation instance.
    /// </summary>
    public string? InstancePrefix { get; set; }

    /// <summary>
    ///     Gets or sets the adapter id
    /// </summary>
    public string? AdapterRtId { get; set; }
    
    /// <summary>
    ///     Gets or sets the adapter ck id
    /// </summary>
    public string? AdapterCkTypeId { get; set; }

    /// <summary>
    ///     Gets or sets the tenant id
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Gets or sets the communication controller services uri
    /// </summary>
    public string? CommunicationControllerServicesUri { get; set; }
    
    /// <summary>
    ///     Gets or sets a value indicating whether the adapter should ignore certificate validation
    /// </summary>
    public bool IgnoreCertificateValidation { get; set; }

    /// <summary>
    ///     Gets or sets the RabbitMQ broker host name
    /// </summary>
    public string BrokerHost { get; set; }

    /// <summary>
    ///     Gets or sets the RabbitMQ broker virtual host
    /// </summary>
    public string BrokerVirtualHost { get; set; }

    /// <summary>
    ///     Gets or sets the RabbitMQ broker port
    /// </summary>
    public ushort BrokerPort { get; set; }

    /// <summary>
    ///     Gets or sets the RabbitMQ broker username
    /// </summary>
    public string? BrokerUsername { get; set; }

    /// <summary>
    ///     Gets or sets the RabbitMQ broker password
    /// </summary>
    public string? BrokerPassword { get; set; }
    
    /// <summary>
    ///    Gets or sets the NLog configuration file
    /// </summary>
    public string NlogConfigPath { get; set; }

    /// <summary>
    /// Defines if the adapter should run as hosted service or be started manually (e.g only when a client connects)
    /// </summary>
    public bool UseHostedService { get; set; } = true;

    /// <summary>
    ///     Enables periodic publishing of CPU / memory / thread snapshots to the communication
    ///     controller (used to drive the UI sparklines). Defaults to true. Set to false on
    ///     resource-constrained edge adapters to suppress the sampler.
    /// </summary>
    public bool MetricsSamplingEnabled { get; set; } = true;

    /// <summary>
    ///     Sampling interval in seconds for the adapter metrics sampler. Clamped to
    ///     at least 1 second; very large values reduce sparkline resolution. Defaults to 10.
    /// </summary>
    public int MetricsSamplingIntervalSeconds { get; set; } = 10;
}