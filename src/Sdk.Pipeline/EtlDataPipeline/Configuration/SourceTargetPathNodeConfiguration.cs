namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Pipeline node configuration including source and target path including write mode
/// </summary>
public record SourceTargetPathNodeConfiguration : TargetPathNodeConfiguration, IPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the source path in json path format
    /// </summary>
    [PropertyGroup("Paths", 0, "jsonpath")]
    public string Path { get; set; } = "$";
}