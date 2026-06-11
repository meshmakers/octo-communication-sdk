namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Exception thrown when a JSONPath expression cannot be parsed or evaluated.
/// </summary>
public class JsonPathException : Exception
{
    /// <summary>
    /// The JSONPath expression that caused the error.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Zero-based character position within <see cref="Path"/> where the error was detected.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPathException"/> class.
    /// </summary>
    /// <param name="message">Human-readable description of the error.</param>
    /// <param name="path">The JSONPath expression that caused the error.</param>
    /// <param name="position">Zero-based character position within <paramref name="path"/> where the error was detected.</param>
    public JsonPathException(string message, string path, int position)
        : base($"{message} (path: '{path}', position {position})")
    {
        Path = path;
        Position = position;
    }
}
