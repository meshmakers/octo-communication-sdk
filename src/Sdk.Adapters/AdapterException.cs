namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Exception that is thrown when an error occurs in the adapter
/// </summary>
public class AdapterException : Exception
{
    /// <inheritdoc />
    public AdapterException()
    {
    }

    /// <inheritdoc />
    public AdapterException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public AdapterException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception ConfigurationErrorAdapterRtIdAdapterCkTypeIdNotSet()
    {
        return new AdapterException("Adapter RtId or Adapter CkTypeId not set");
    }

    /// <summary>
    ///     AB#5303 item 4 — the configured datastore hostnames do not resolve. Naming the
    ///     configuration KEY alongside the value is the point: the lease-time failure this replaces
    ///     named neither, and an operator reading it could not tell which setting was wrong.
    /// </summary>
    internal static Exception DatastoreHostsDoNotResolve(IReadOnlyCollection<string> unresolved)
    {
        return new AdapterException(
            $"Datastore hostname(s) do not resolve: {string.Join("; ", unresolved)}. Refusing to start. " +
            "These names are resolved, not connected to - a Kubernetes Service resolves while its pods " +
            "are down - so this is a configuration error, not an outage. Check the operator's " +
            "ClusterDependencies against the services actually present in this cluster (AB#5303).");
    }

    /// <summary>
    ///     AB#5303 item 5 — the deployment configured the <c>AdapterPool</c> section and the process
    ///     is still not a pool member. Naming both sides is the point: the keys that arrived say what
    ///     the deployment emitted, the unbound properties say what the SDK expected, and the
    ///     difference between them is the whole defect.
    /// </summary>
    internal static Exception PoolMemberConfigurationDoesNotBind(
        IReadOnlyCollection<string> configuredKeys, IReadOnlyCollection<string> unboundProperties)
    {
        var expected = string.Join(", ",
            unboundProperties.Select(AdapterPoolMemberConfigurationGuard.EnvironmentVariableFor));

        return new AdapterException(
            $"Configuration section '{AdapterPoolMemberOptions.SectionName}' is set " +
            $"({string.Join(", ", configuredKeys)}), but {string.Join(" and ", unboundProperties)} did not bind, " +
            $"so this process is not an adapter pool member. Expected {expected}. " +
            "Refusing to start: without them the process runs as an ordinary adapter with no tenant and no " +
            "AdapterRtId — Running, Ready, and serving no pool (AB#4924, AB#5303).");
    }
}
