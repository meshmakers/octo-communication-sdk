using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Clears <see cref="AdapterOptions.DedicatedTenantId" /> on a process that is composed as an
///     adapter pool member (AB#4924).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Three call sites in two repositories justify their safety with the sentence "it is
///         null on a pool member", and without this type not one of them was true.</b>
///         <see cref="AdapterOptions" />' constructor sets <c>DedicatedTenantId = "meshTest"</c>, so a
///         member that was given no tenant carried <c>"meshTest"</c>, and the local runbook — until it
///         was corrected alongside this fix — instructed the operator to set
///         <c>OCTO_ADAPTER__DEDICATEDTENANTID</c> to the <b>lending</b> tenant, which made it carry
///         the lender's id while executing a borrower's pipeline. The worst of the three reads is
///         <c>ServiceAccountTokenService.ResolveTenantId</c>: a service-account configuration that
///         names no tenant falls back to this value, so a leased execution would have acquired a
///         token for the lender rather than declining.
///     </para>
///     <para>
///         The member's own connection credential is unaffected: it is derived from
///         <see cref="AdapterPoolMemberOptions.PoolTenantId" /> by
///         <c>ConfigureAdapterAuthenticatorOptions</c>, not from this property. Nothing else on a
///         member reads it — <c>AdapterHubClient</c> and <c>AdapterExecutionService</c>, the two
///         consumers that need a dedicated tenant, are not registered at all in the pool-member
///         branch of either adapter builder.
///     </para>
///     <para>
///         Registered by <c>AddAdapterPoolMember()</c> and gated on
///         <see cref="AdapterPoolMemberOptions.IsEnabled" /> rather than on the call, because a host
///         may compose the member services while the configuration names no pool; that process is a
///         dedicated adapter and must keep its tenant. It is the counterpart of
///         <c>ConfigureLegacyAdapterTenantId</c>, which both builders already skip on a member.
///     </para>
/// </remarks>
public sealed class ConfigurePoolMemberAdapterTenantId : IPostConfigureOptions<AdapterOptions>
{
    private readonly ILogger<ConfigurePoolMemberAdapterTenantId> _logger;
    private readonly IOptions<AdapterPoolMemberOptions> _poolMemberOptions;

    /// <summary>
    ///     Creates a new instance of <see cref="ConfigurePoolMemberAdapterTenantId" />.
    /// </summary>
    public ConfigurePoolMemberAdapterTenantId(IOptions<AdapterPoolMemberOptions> poolMemberOptions,
        ILogger<ConfigurePoolMemberAdapterTenantId> logger)
    {
        _poolMemberOptions = poolMemberOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, AdapterOptions options)
    {
        var poolMember = _poolMemberOptions.Value;
        if (!poolMember.IsEnabled)
        {
            return;
        }

        var configured = options.DedicatedTenantId;
        options.DedicatedTenantId = null;

        // Only say something when a value was actually configured. The constructor default reaches
        // every member and is not the operator's doing, so warning about it would train the warning
        // away; an explicit value, on the other hand, is a chart or an environment that believes this
        // member serves a tenant, and that belief is what has to be corrected.
        if (!string.IsNullOrWhiteSpace(configured) &&
            !string.Equals(configured, AdapterOptions.UnconfiguredTenantDefault, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "This process is an adapter pool member of pool '{PoolRtId}' in tenant '{PoolTenantId}', so " +
                "OCTO_ADAPTER__DEDICATEDTENANTID ('{DedicatedTenantId}') has been cleared. A member executes " +
                "only for the tenant of the lease it currently holds; its own connection tenant is the " +
                "lender and is derived from the pool configuration. Remove the setting (AB#4924).",
                poolMember.PoolRtId, poolMember.PoolTenantId, configured);
        }
    }
}
