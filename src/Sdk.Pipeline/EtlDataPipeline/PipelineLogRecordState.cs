using System.Collections;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// The logger state of a record written through <see cref="IPipelineLogger.Log" /> (AB#9335): a
/// fixed body plus structured attributes.
/// </summary>
/// <remarks>
/// The state is a key/value list on purpose: NLog (the adapters' provider) and every other
/// structured provider turn its entries into properties of the record. It deliberately has no
/// <c>{OriginalFormat}</c> entry, so the body is taken from the formatter as it is and never parsed
/// as a message template - a <c>{0}</c> or <c>{name}</c> in a pipeline value is logged verbatim.
/// </remarks>
internal sealed class PipelineLogRecordState(string body, IReadOnlyList<KeyValuePair<string, object?>> attributes)
    : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>The body of the record.</summary>
    public string Body { get; } = body;

    /// <inheritdoc />
    public int Count => attributes.Count;

    /// <inheritdoc />
    public KeyValuePair<string, object?> this[int index] => attributes[index];

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => Body;

    /// <summary>
    /// Maps the level to the four levels pipeline logging uses: Trace becomes Debug, Critical becomes
    /// Error and None becomes Information.
    /// </summary>
    internal static LogLevel Normalize(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => LogLevel.Debug,
            LogLevel.Warning => LogLevel.Warning,
            LogLevel.Error or LogLevel.Critical => LogLevel.Error,
            _ => LogLevel.Information
        };
    }
}
