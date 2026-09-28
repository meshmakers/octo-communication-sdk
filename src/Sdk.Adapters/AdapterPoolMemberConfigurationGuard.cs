using Microsoft.Extensions.Configuration;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Binds <see cref="AdapterPoolMemberOptions" /> and refuses to start a process that was told to
///     be a pool member but did not become one (AB#5303 item 5).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>The failure this exists for is silent by construction.</b> Being a pool member is
///         decided by <see cref="AdapterPoolMemberOptions.IsEnabled" />, which reads the very options
///         that can fail to bind. When they do, nothing is missing from the process's point of view:
///         it composes as an ordinary adapter, comes up <c>1/1 Ready</c>, passes its health check and
///         is not a pool member. Observed on a local kind cluster, where the chart emitted
///         <c>OCTO_ADAPTERPOOL__POOLTENANTID</c> while the SDK binds
///         <c>AdapterPoolTenantId</c> — the pod logged <c>AdapterRtId NULL</c> and joined the plain
///         adapter hub. The chart was fixed (octo-mesh-adapter <c>75b14a2</c>); this is the half that
///         makes the next such drift loud instead of invisible.
///     </para>
///     <para>
///         The independent signal is the <b>section</b>, not the properties. A configured-but-unbound
///         member still puts its keys into <c>AdapterPool</c>; only their names are wrong. So: keys
///         present with values, yet <see cref="AdapterPoolMemberOptions.IsEnabled" /> false, means the
///         deployment asked for a member and the binding did not deliver one. An empty section is not
///         a claim and stays a plain adapter — which is what every dedicated adapter has.
///     </para>
/// </remarks>
public static class AdapterPoolMemberConfigurationGuard
{
    /// <summary>
    ///     Binds the <c>AdapterPool</c> section and throws when it is configured but does not yield a
    ///     usable member configuration.
    /// </summary>
    /// <exception cref="AdapterException">
    ///     The section carries values and <see cref="AdapterPoolMemberOptions.IsEnabled" /> is still
    ///     false — the process would degrade into an ordinary adapter.
    /// </exception>
    public static AdapterPoolMemberOptions BindAndVerify(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(AdapterPoolMemberOptions.SectionName);

        var options = new AdapterPoolMemberOptions();
        section.Bind(options);

        if (options.IsEnabled)
        {
            return options;
        }

        // Whitespace-only counts as unset here for the same reason IsEnabled treats it that way: a
        // chart that renders an empty value is not asking for a pool member.
        var configuredKeys = section.GetChildren()
            .Where(child => !string.IsNullOrWhiteSpace(child.Value))
            .Select(child => child.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configuredKeys.Length == 0)
        {
            // Nothing was claimed. This is every dedicated adapter.
            return options;
        }

        var unbound = new List<string>(2);
        if (string.IsNullOrWhiteSpace(options.AdapterPoolTenantId))
        {
            unbound.Add(nameof(AdapterPoolMemberOptions.AdapterPoolTenantId));
        }

        if (string.IsNullOrWhiteSpace(options.AdapterPoolRtId))
        {
            unbound.Add(nameof(AdapterPoolMemberOptions.AdapterPoolRtId));
        }

        throw AdapterException.PoolMemberConfigurationDoesNotBind(configuredKeys, unbound);
    }

    /// <summary>The environment variable an <c>AdapterPool</c> property is read from.</summary>
    internal static string EnvironmentVariableFor(string propertyName)
    {
        return $"OCTO_{AdapterPoolMemberOptions.SectionName.ToUpperInvariant()}__{propertyName.ToUpperInvariant()}";
    }
}
