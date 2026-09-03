using Microsoft.Extensions.Logging;

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
    ///     Public issuer URI of the identity service the adapter authenticates its own
    ///     <c>/{tenantId}/adapterHub</c> connection against (AB#5072). OIDC discovery runs against
    ///     it, so it must be the address the identity service itself issues tokens under — not a
    ///     cluster-internal service name whose discovery document would advertise a different issuer
    ///     than the communication controller validates.
    /// </summary>
    /// <remarks>
    ///     ⚠️ On <c>octo-mesh-adapter</c> this is a second key next to
    ///     <c>Adapter:AuthorityUrl</c> (<c>MeshAdapterConfiguration.AuthorityUrl</c>), which binds
    ///     the same configuration section and names the same identity service — but for the
    ///     <b>inbound</b> direction (the issuer secured <c>FromHttpRequest@2</c> routes accept). That
    ///     type lives in the adapter repository and is unknown to this SDK, and adapters without it
    ///     (Loxone, Modbus, Zenon, the simulation plug) still need an issuer here, so the outbound
    ///     credential carries its own key. They normally hold the same value.
    /// </remarks>
    public string? IssuerUri { get; set; }

    /// <summary>
    ///     Client id of the confidential OAuth client representing this adapter (AB#5072). When
    ///     empty, no token is acquired and the adapter connects to the controller's adapter hub
    ///     unauthenticated — exactly as every adapter in the estate does today.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    ///     Client secret for <see cref="ClientId" />. Supply it through a
    ///     <c>secretKeyRef</c>-backed environment variable the same way
    ///     <c>OCTO_ADAPTER__BROKERPASSWORD</c> is supplied — never as a literal in a values file.
    ///     The SDK never writes it to any log, not even truncated.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    ///     Issuer values accepted in the identity service's discovery document in addition to
    ///     <see cref="IssuerUri" /> (AB#5081). Empty by default, i.e. the document's <c>issuer</c>
    ///     must equal the address the adapter was pointed at.
    /// </summary>
    /// <remarks>
    ///     Bound from the same <c>Adapter:AdditionalValidIssuers</c> key the inbound direction
    ///     already uses (<c>OCTO_ADAPTER__ADDITIONALVALIDISSUERS__0</c>), on purpose: it is the same
    ///     fact about the deployment — "this identity service is also known under these names" —
    ///     and an adapter that needs it for the tokens it accepts needs it for the token it fetches.
    ///     Sharing the key means a split-horizon installation that already works inbound needs no
    ///     new setting to work outbound.
    ///     <para>
    ///         The case it exists for: an adapter in a container reaching the host's identity service
    ///         as <c>https://mac.local:5003</c> while that service advertises
    ///         <c>https://localhost:5003/</c>. Without it, discovery fails with "Issuer name does not
    ///         match authority" and no token is ever acquired.
    ///     </para>
    /// </remarks>
    public string[] AdditionalValidIssuers { get; set; } = [];

    /// <summary>
    ///     Whether enough is configured to attempt a token request at all (AB#5072).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The secret is deliberately not part of the check — a public client has none, and a
    ///         confidential client with a missing secret must fail loudly at the token endpoint
    ///         rather than silently degrade to an anonymous connection that looks healthy until the
    ///         adapter-hub gate (AB#5063) is armed.
    ///     </para>
    ///     <para>
    ///         🔴 <b>Unconfigured is a supported state and must stay one.</b> Every adapter in the
    ///         estate runs without these keys today; a hard requirement here would take the whole
    ///         fleet down on upgrade. <see cref="TenantId" /> is not part of the check either: it
    ///         always carries a value (the adapter cannot address its own hub route without one) and
    ///         gating on it would only hide a misconfiguration behind an anonymous connection.
    ///     </para>
    /// </remarks>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(IssuerUri) && !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>
    ///     When true (the default), the adapter eagerly warms the tenant's CK model cache right
    ///     after startup instead of paying the model load on the first pipeline execution
    ///     (AB#4920, on-demand lifecycle Epic AB#4914 — after a wake from 0 replicas the first
    ///     request would otherwise carry the full model-load latency). The warm-up runs in the
    ///     background and never blocks startup or readiness; set to false to restore the pure
    ///     lazy-load behaviour (env <c>OCTO_ADAPTER__EAGERCKMODELLOAD=false</c>).
    /// </summary>
    public bool EagerCkModelLoad { get; set; } = true;

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
    ///     Minimum log level for the adapter, applied centrally by the SDK adapter host so every
    ///     adapter shares the same default (Information) instead of the per-repo nlog.config
    ///     <c>minlevel="Debug"</c>. The host wires NLog with <c>RemoveLoggerFactoryFilter = false</c>
    ///     so this Microsoft.Extensions.Logging level actually governs NLog output. Override per
    ///     deployment via <c>OCTO_ADAPTER__MINIMUMLOGLEVEL=Debug</c> (no image rebuild needed) when
    ///     you need verbose troubleshooting.
    /// </summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

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