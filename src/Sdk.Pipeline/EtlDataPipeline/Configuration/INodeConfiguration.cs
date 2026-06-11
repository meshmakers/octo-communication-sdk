namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Interface for node configuration
/// </summary>
public interface INodeConfiguration
{
    /// <summary>
    /// Gets or sets an optional description
    /// </summary>
    string? Description { get; set; }
}