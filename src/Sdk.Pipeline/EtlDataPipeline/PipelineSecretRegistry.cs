using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// The plaintext values one pipeline execution must never show in its diagnostics (AB#5528 / AB#5538):
/// the plaintext <c>RevealSecret@1</c> wrote into the data context and the credentials nodes resolved
/// from configuration. The values themselves still flow through the data context unchanged — a node
/// that needs the plaintext (an HTTP call, an SMTP login) gets it — but every diagnostic surface
/// replaces them with <see cref="Mask" />: debug snapshots and dry-run intents
/// (<see cref="Debugger.DefaultPipelineDebugger" />), the execution log written through
/// <see cref="Nodes.INodeContext" /> and the persisted execution result
/// (<c>SetPipelineExecutionResult@1</c>).
/// </summary>
/// <remarks>
/// <para>
/// Redaction is by <b>value</b>, not by path: a revealed secret copied to another path, concatenated
/// into a header (<c>Bearer …</c>) or carried into a child context of a loop is masked as well, which a
/// path marker could not follow. A string that equals a registered value becomes <see cref="Mask" />;
/// a string that contains one has each occurrence replaced, but only for values of at least
/// <see cref="MinimumSubstringLength" /> characters — a one-character "secret" masked inside every
/// string would make every snapshot unreadable and tells an attacker nothing anyway.
/// </para>
/// <para>
/// One instance belongs to one execution: the root <see cref="Nodes.NodeContext" /> creates it and
/// every child context shares it, so nothing registered in one run is masked (or remembered) in the
/// next. Thread-safe — parallel <c>ForEach@1</c> iterations register concurrently.
/// </para>
/// </remarks>
public sealed class PipelineSecretRegistry
{
    /// <summary>
    /// Text that replaces a registered value in every diagnostic output.
    /// </summary>
    public const string Mask = "***";

    /// <summary>
    /// Shortest value that is also masked <b>inside</b> longer strings; shorter values are masked only
    /// where a string equals them.
    /// </summary>
    public const int MinimumSubstringLength = 4;

    private readonly ConcurrentDictionary<string, byte> _values = new(StringComparer.Ordinal);

    // Longest first, so a value that contains another registered value is replaced as a whole.
    // Rebuilt lazily after a registration; registrations are rare (a handful per execution).
    private volatile string[]? _orderedValues;

    /// <summary>
    /// True when at least one value is registered; every redaction is a no-op otherwise.
    /// </summary>
    public bool HasSecrets => !_values.IsEmpty;

    /// <summary>
    /// Registers a plaintext that must not appear in diagnostics of this execution. Null, empty and
    /// whitespace-only values are ignored.
    /// </summary>
    /// <param name="plaintext">The value</param>
    public void Register(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return;
        }

        if (_values.TryAdd(plaintext, 0))
        {
            _orderedValues = null;
        }
    }

    /// <summary>
    /// Returns <paramref name="text" /> with every registered value masked (see the class remarks).
    /// </summary>
    /// <param name="text">The text</param>
    /// <returns>The redacted text; the same instance when nothing had to be masked</returns>
    public string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || _values.IsEmpty)
        {
            return text;
        }

        if (_values.ContainsKey(text))
        {
            return Mask;
        }

        var result = text;
        foreach (var value in GetOrderedValues())
        {
            if (value.Length >= MinimumSubstringLength && result.Contains(value, StringComparison.Ordinal))
            {
                result = result.Replace(value, Mask, StringComparison.Ordinal);
            }
        }

        return result;
    }

    /// <summary>
    /// Redacts a log argument: strings are masked, a <see cref="JsonNode" /> is redacted like a
    /// snapshot, anything else is returned unchanged.
    /// </summary>
    /// <param name="argument">The argument</param>
    /// <returns>The redacted argument</returns>
    public object? RedactArgument(object? argument)
    {
        return argument switch
        {
            string text => Redact(text),
            JsonNode node => Redact(node),
            _ => argument
        };
    }

    /// <summary>
    /// Returns <paramref name="node" /> with every string value masked (see the class remarks). The
    /// input is never modified: when something has to be masked a redacted deep clone is returned,
    /// otherwise the same instance — so the common case (no secret in the document) allocates nothing.
    /// </summary>
    /// <param name="node">The JSON tree</param>
    /// <returns>The redacted tree</returns>
    public JsonNode? Redact(JsonNode? node)
    {
        if (node == null || _values.IsEmpty || !NeedsRedaction(node))
        {
            return node;
        }

        var clone = node.DeepClone();
        if (clone is JsonValue)
        {
            // A bare string root cannot be replaced in place - it has no parent.
            return JsonValue.Create(Redact(clone.GetValue<string>()));
        }

        RedactInPlace(clone);
        return clone;
    }

    private bool NeedsRedaction(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (property.Value != null && NeedsRedaction(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item != null && NeedsRedaction(item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                if (node.GetValueKind() != JsonValueKind.String)
                {
                    return false;
                }

                var text = node.GetValue<string>();
                return !ReferenceEquals(Redact(text), text);
        }
    }

    private void RedactInPlace(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Materialise the keys first: assigning a property while enumerating throws.
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                    {
                        var text = value.GetValue<string>();
                        var redacted = Redact(text);
                        if (!ReferenceEquals(redacted, text))
                        {
                            obj[key] = redacted;
                        }
                    }
                    else if (child != null)
                    {
                        RedactInPlace(child);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                    {
                        var text = value.GetValue<string>();
                        var redacted = Redact(text);
                        if (!ReferenceEquals(redacted, text))
                        {
                            array[i] = redacted;
                        }
                    }
                    else if (child != null)
                    {
                        RedactInPlace(child);
                    }
                }

                break;
        }
    }

    private string[] GetOrderedValues()
    {
        var ordered = _orderedValues;
        if (ordered != null)
        {
            return ordered;
        }

        ordered = _values.Keys.OrderByDescending(v => v.Length).ToArray();
        _orderedValues = ordered;
        return ordered;
    }
}
