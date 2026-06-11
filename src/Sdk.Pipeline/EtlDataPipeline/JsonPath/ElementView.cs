using System.Collections.Generic;
using System.Text.Json;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// <see cref="IJsonView{TSelf}"/> over <see cref="JsonElement"/> (zero-copy read view).
/// </summary>
internal readonly struct ElementView(JsonElement e) : IJsonView<ElementView>
{
    private readonly JsonElement _e = e;

    /// <summary>
    /// The underlying <see cref="JsonElement"/> this view wraps. Lets callers that
    /// know their view is an <see cref="ElementView"/> (e.g. <c>ElementSource</c>) read
    /// the element directly — for <c>ValueKind</c> inspection or scalar conversion —
    /// without re-parsing, preserving the zero-copy read path.
    /// </summary>
    public JsonElement Element => _e;

    public JsonValueKind Kind => _e.ValueKind;

    public bool TryGetProperty(string name, out ElementView child)
    {
        if (_e.ValueKind == JsonValueKind.Object && _e.TryGetProperty(name, out var c))
        {
            child = new ElementView(c);
            return true;
        }

        child = default;
        return false;
    }

    public bool TryGetIndex(int index, out ElementView child)
    {
        if (_e.ValueKind == JsonValueKind.Array && index >= 0 && index < _e.GetArrayLength())
        {
            child = new ElementView(_e[index]);
            return true;
        }

        child = default;
        return false;
    }

    public int ArrayLength => _e.ValueKind == JsonValueKind.Array ? _e.GetArrayLength() : 0;

    public IEnumerable<(string Key, ElementView Value)> EnumerateChildren()
    {
        switch (_e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in _e.EnumerateArray())
                {
                    yield return ("", new ElementView(item));
                }

                break;
            case JsonValueKind.Object:
                foreach (var prop in _e.EnumerateObject())
                {
                    yield return (prop.Name, new ElementView(prop.Value));
                }

                break;
        }
    }

    public string GetRawText() => _e.GetRawText();

    public bool TryGetString(out string value)
    {
        if (_e.ValueKind == JsonValueKind.String)
        {
            value = _e.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
