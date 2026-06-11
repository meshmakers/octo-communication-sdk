namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Pipeline node configuration including source path
/// </summary>
public record PathNodeConfiguration : NodeConfiguration, IPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the source path in json path format
    /// </summary>
    [PropertyGroup("Paths", 0, "jsonpath")]
    public string Path { get; set; } = "$";
}