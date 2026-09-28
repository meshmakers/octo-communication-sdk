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
