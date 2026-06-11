namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Interface for target path node configuration
/// </summary>
public interface IPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the source path in json path format
    /// </summary>
    string Path { get; set; }
}