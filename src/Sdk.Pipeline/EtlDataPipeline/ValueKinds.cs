namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Defines the kind of value.
/// </summary>
public enum ValueKinds
{
    /// <summary>
    /// The value is a simple value.
    /// </summary>
    Simple = 0,
    
    /// <summary>
    /// The value is an array.
    /// </summary>
    Array = 1,
}