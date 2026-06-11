namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Write mode for the value.
/// </summary>
public enum TargetValueWriteModes
{
    /// <summary>
    /// Overwrite the existing value.
    /// </summary>
    Overwrite = 0,
    
    /// <summary>
    /// Append to the existing value.
    /// </summary>
    Append = 1,
    
    /// <summary>
    /// Prepend to the existing value.
    /// </summary>
    Prepend = 2,

    /// <summary>
    /// Merges the existing document and the new document.
    /// </summary>
    Merge = 3
}