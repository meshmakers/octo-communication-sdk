using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Binds the legacy <c>Adapter:TenantId</c> configuration key onto
///     <see cref="AdapterOptions.DedicatedTenantId" /> for one deprecation release (AB#4924,
///     increment 3).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Without this, renaming the property would have silently un-configured the entire
///         adapter fleet.</b> <c>OCTO_ADAPTER__TENANTID</c> is set by eight Helm charts
///         (<c>octo-mesh-adapter</c>, <c>octo-loxone-adapter</c>, <c>octo-plug-simulation</c>,
///         <c>octo-weclapp-adapter</c>, <c>octo-modbus-plug</c>, <c>octo-modbus-socket</c>,
///         <c>octo-finapi-adapter</c>, <c>octo-eda-adapter</c>). The options binder ignores a key
///         with no matching property <b>without complaint</b>, so every one of those adapters would
///         have fallen back to the <c>"meshTest"</c> default and connected on the wrong tenant
///         route — a failure that reports nothing at startup and only surfaces as a 403 on the hub,
///         or worse, as a successful connection to a tenant that happens to exist.
///     </para>
///     <para>
///         The new key <c>OCTO_ADAPTER__DEDICATEDTENANTID</c> wins when both are set. Remove this
///         type once every chart in the estate has been moved to the new key; the warning below is
///         what tells you which ones have not.
///     </para>
/// </remarks>
public sealed class ConfigureLegacyAdapterTenantId : IPostConfigureOptions<AdapterOptions>
{
    /// <summary>
    ///     The default <see cref="AdapterOptions.DedicatedTenantId" /> set by the constructor.
    ///     Treated as "not configured" here, so the legacy key still wins over it.
    /// </summary>
    private const string UnconfiguredDefault = "meshTest";

    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigureLegacyAdapterTenantId> _logger;

    /// <summary>
    ///     Creates a new instance of <see cref="ConfigureLegacyAdapterTenantId" />.
    /// </summary>
    public ConfigureLegacyAdapterTenantId(IConfiguration configuration,
        ILogger<ConfigureLegacyAdapterTenantId> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, AdapterOptions options)
    {
        var legacyValue = _configuration["Adapter:TenantId"];
        if (string.IsNullOrWhiteSpace(legacyValue))
        {
            return;
        }

        var newKeyConfigured = !string.IsNullOrWhiteSpace(options.DedicatedTenantId)
                               && !string.Equals(options.DedicatedTenantId, UnconfiguredDefault,
                                   StringComparison.Ordinal);

        if (newKeyConfigured)
        {
            // A chart carrying both keys is mid-migration. Only say something when they disagree —
            // that is the case where the adapter is not running where its chart appears to say.
            if (!string.Equals(options.DedicatedTenantId, legacyValue, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Both OCTO_ADAPTER__DEDICATEDTENANTID ({DedicatedTenantId}) and the deprecated " +
                    "OCTO_ADAPTER__TENANTID ({LegacyTenantId}) are set and they disagree. The new key " +
                    "wins. Remove OCTO_ADAPTER__TENANTID from this adapter's chart (AB#4924).",
                    options.DedicatedTenantId, legacyValue);
            }

            return;
        }

        options.DedicatedTenantId = legacyValue;
        _logger.LogWarning(
            "OCTO_ADAPTER__TENANTID is deprecated and will be removed in the next release. This " +
            "adapter is running on tenant {TenantId} from the legacy key; update its chart to set " +
            "OCTO_ADAPTER__DEDICATEDTENANTID instead (AB#4924).",
            legacyValue);
    }
}
