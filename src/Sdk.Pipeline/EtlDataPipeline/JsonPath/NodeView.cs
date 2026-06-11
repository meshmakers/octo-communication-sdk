using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// <see cref="IJsonView{TSelf}"/> over <see cref="JsonNode"/> (mutable in-memory tree).
/// A null node represents a JSON null value.
/// </summary>
internal readonly struct NodeView(JsonNode? n) : IJsonView<NodeView>
{
    private readonly JsonNode? _n = n;

    public JsonNode? Node => _n;

    public JsonValueKind Kind => _n?.GetValueKind() ?? JsonValueKind.Null;

    public bool TryGetProperty(string name, out NodeView child)
    {
        if (_n is JsonObject obj && obj.TryGetPropertyValue(name, out var c))
        {
            child = new NodeView(c);
            return true;
        }

        child = default;
        return false;
    }

    public bool TryGetIndex(int index, out NodeView child)
    {
        if (_n is JsonArray arr && index >= 0 && index < arr.Count)
        {
            child = new NodeView(arr[index]);
            return true;
        }

        child = default;
        return false;
    }

    public int ArrayLength => _n is JsonArray arr ? arr.Count : 0;

    public IEnumerable<(string Key, NodeView Value)> EnumerateChildren()
    {
        switch (_n)
        {
            case JsonArray arr:
                foreach (var item in arr)
                {
                    yield return ("", new NodeView(item));
                }

                break;
            case JsonObject obj:
                foreach (var prop in obj)
                {
                    yield return (prop.Key, new NodeView(prop.Value));
                }

                break;
        }
    }

    public string GetRawText() => _n?.ToJsonString() ?? "null";

    public bool TryGetString(out string value)
    {
        if (_n is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            value = s;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
