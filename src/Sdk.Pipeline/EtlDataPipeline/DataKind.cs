namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Classification of the value found at a path in an <see cref="IDataContext"/>.
/// </summary>
public enum DataKind
{
    /// <summary>The path does not exist or its kind cannot be determined.</summary>
    Undefined,

    /// <summary>The value at the path is JSON null.</summary>
    Null,

    /// <summary>The value at the path is a JSON object.</summary>
    Object,

    /// <summary>The value at the path is a JSON array.</summary>
    Array,

    /// <summary>The value at the path is a JSON string.</summary>
    String,

    /// <summary>The value at the path is a JSON number.</summary>
    Number,

    /// <summary>The value at the path is a JSON boolean.</summary>
    Boolean
}
