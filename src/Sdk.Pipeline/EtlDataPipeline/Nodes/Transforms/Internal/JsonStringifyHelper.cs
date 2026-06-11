using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Internal;

/// <summary>
/// Stringifies <see cref="JsonNode"/> values matching the format produced by
/// <c>JToken.ToString()</c> (Newtonsoft.Json). Required by FormatStringNode,
/// ConcatNode, HashNode for parity / hash stability with pre-migration output.
///
/// Differences from <see cref="JsonNode.ToJsonString"/>:
/// - Booleans render as "True"/"False" (capitalized) instead of "true"/"false".
/// - Numbers render via the raw JSON token (preserves "1" vs "1.0", invariant culture).
/// - Strings render unquoted (their underlying value).
/// - Null renders as null reference (caller's responsibility).
/// - Object/Array render with 2-space indentation and "\n" newlines, matching
///   Newtonsoft's <c>Formatting.Indented</c> default. Hash stability for object/array
///   sources across the Newtonsoft→STJ migration depends on this exact format.
/// </summary>
internal static class JsonStringifyHelper
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        // Force Unix-style newline so output is byte-identical across platforms
        // and matches Newtonsoft's hard-coded "\n" in Formatting.Indented output.
        NewLine = "\n",
        IndentCharacter = ' ',
        IndentSize = 2,
        // STJ's default encoder (JavaScriptEncoder.Default) escapes all non-ASCII (ü→ü)
        // and HTML-sensitive chars (&→&, <→<). Newtonsoft's Formatting.Indented emits
        // them literally, so the default encoder would silently change HashNode output for any
        // object/array source containing umlauts — routine on a German industrial platform.
        // UnsafeRelaxedJsonEscaping still escapes control chars / " / \ but emits non-ASCII and
        // HTML chars literally: the closest STJ match to Newtonsoft's default.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Converts a <see cref="JsonNode"/> to its Newtonsoft <c>JToken.ToString()</c>-parity
    /// string representation. Returns <see langword="null"/> for null/JSON-null input.
    /// </summary>
    public static string? ToLegacyString(JsonNode? node)
    {
        if (node is null) return null;
        var kind = node.GetValueKind();
        return kind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.Number => node.ToJsonString(),
            // Object/Array use indented output for Newtonsoft parity.
            _ => node.ToJsonString(IndentedOptions)
        };
    }
}
