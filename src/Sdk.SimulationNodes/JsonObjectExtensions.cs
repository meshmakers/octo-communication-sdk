using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Meshmakers.Octo.Sdk.SimulationNodes;

internal static class JsonObjectExtensions
{
    public static T GetValue<T>(this JsonObject jsonObject, string key, T defaultValue)
    {
        if (!jsonObject.TryGetPropertyValue(key, out var node) || node is null)
        {
            return defaultValue;
        }

        // A present-but-unconvertible value is a configuration error: surface it (let
        // Deserialize throw) rather than silently masking it as the default. The default is
        // only the fallback for an absent/null key (handled above).
        var deserialized = node.Deserialize<T>(SystemTextJsonOptions.Default);
        return deserialized ?? defaultValue;
    }
}
