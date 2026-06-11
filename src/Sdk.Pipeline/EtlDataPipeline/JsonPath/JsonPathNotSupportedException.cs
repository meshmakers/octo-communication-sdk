namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Exception thrown when a JSONPath expression uses a feature that the OctoMesh evaluator does not support.
/// </summary>
public sealed class JsonPathNotSupportedException : JsonPathException
{
    /// <summary>
    /// Name of the unsupported JSONPath feature.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPathNotSupportedException"/> class.
    /// </summary>
    /// <param name="feature">Name of the unsupported JSONPath feature.</param>
    /// <param name="path">The JSONPath expression that caused the error.</param>
    /// <param name="position">Zero-based character position within <paramref name="path"/> where the unsupported feature occurs.</param>
    public JsonPathNotSupportedException(string feature, string path, int position)
        : base($"JSONPath feature '{feature}' is not supported by the OctoMesh evaluator", path, position)
    {
        Feature = feature;
    }
}
