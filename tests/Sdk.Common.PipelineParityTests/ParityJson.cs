using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Shared JSON canonicalization for the parity tests: re-serialize through default STJ formatting so
/// whitespace differences between Newtonsoft and STJ output do not produce false negatives. Values
/// compared through this helper are kept integral/string/bool so trailing-zero number normalization
/// (see <see cref="ReadParityTests"/>) is not required.
/// </summary>
internal static class ParityJson
{
    /// <summary>Whitespace-canonical form of any JSON text.</summary>
    public static string Canonicalize(string json)
    {
        using var d = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(d.RootElement);
    }

    /// <summary>Canonical form of a Newtonsoft token (the oracle side).</summary>
    public static string Nso(JToken token) => Canonicalize(token.ToString(Newtonsoft.Json.Formatting.None));
}
