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
}
