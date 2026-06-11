using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Single source of truth for the <see cref="JsonValueKind"/>/<see cref="JsonNode"/>
/// → <see cref="DataKind"/> classification shared by the read sources
/// (<c>ElementSource</c>, <c>LayeredSource</c>). Keeps the zero-copy
/// <see cref="JsonElement"/> path and the lifted <see cref="JsonNode"/> path mapping
/// identically.
/// </summary>
internal static class DataKindMapper
{
    /// <summary>Classifies a zero-copy <see cref="JsonElement"/> by its value kind.</summary>
    public static DataKind KindOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => DataKind.Object,
        JsonValueKind.Array => DataKind.Array,
        JsonValueKind.String => DataKind.String,
        JsonValueKind.Number => DataKind.Number,
        JsonValueKind.True or JsonValueKind.False => DataKind.Boolean,
        JsonValueKind.Null => DataKind.Null,
        _ => DataKind.Undefined
    };

    /// <summary>Classifies a lifted <see cref="JsonNode"/> by its node type / value kind.</summary>
    public static DataKind KindOf(JsonNode node) => node switch
    {
        JsonObject => DataKind.Object,
        JsonArray => DataKind.Array,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => DataKind.String,
            JsonValueKind.Number => DataKind.Number,
            JsonValueKind.True or JsonValueKind.False => DataKind.Boolean,
            JsonValueKind.Null => DataKind.Null,
            _ => DataKind.Undefined
        },
        _ => DataKind.Undefined
    };
}
