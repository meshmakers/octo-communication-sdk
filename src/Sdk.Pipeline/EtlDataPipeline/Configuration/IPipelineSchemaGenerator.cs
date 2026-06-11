namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Generates a composite JSON Schema for the complete pipeline definition,
/// suitable for Monaco editor autocompletion and validation.
/// </summary>
public interface IPipelineSchemaGenerator
{
    /// <summary>
    /// Generates a composite JSON Schema describing the full pipeline structure
    /// (triggers + transformations) with discriminated type fields for each node.
    /// The schema is cached after first generation.
    /// </summary>
    /// <returns>A JSON string containing the composite JSON Schema</returns>
    string GenerateSchema();
}
