namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Pipeline node configuration including target path including write mode
/// </summary>
public record TargetPathNodeConfiguration : NodeConfiguration, ITargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the target path in json path format
    /// </summary>
    [PropertyGroup("Paths", 1, "jsonpath")]
    public string TargetPath { get; set; } = "$";

    /// <summary>
    /// Gets or sets the writing mode (overwrite, append, prepend)
    /// </summary>
    [PropertyGroup("Write Mode", 5)]
    public TargetValueWriteModes TargetValueWriteMode { get; set; } = TargetValueWriteModes.Overwrite;

    /// <summary>
    /// Gets or sets the value kind to write (simple value or array)
    /// </summary>
    [PropertyGroup("Write Mode", 6)]
    public ValueKinds TargetValueKind { get; set; } = ValueKinds.Simple;

    /// <summary>
    /// Gets or sets the document mode (extend, replace)
    /// </summary>
    [PropertyGroup("Write Mode", 7)]
    public DocumentModes DocumentMode { get; set; } = DocumentModes.Extend;
}