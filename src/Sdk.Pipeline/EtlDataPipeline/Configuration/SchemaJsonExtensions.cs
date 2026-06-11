using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Build-time schema-generation helpers over <see cref="JsonNode"/>. This is framework code below
/// the node-author surface (the schema generators stay JsonNode-based by design); the helper just
/// removes the repeated <c>is JsonValue v &amp;&amp; v.TryGetValue&lt;string&gt;(out s)</c> idiom.
/// </summary>
internal static class SchemaJsonExtensions
{
    /// <summary>
    /// Returns the string value of <paramref name="node"/> when it is a string-valued
    /// <see cref="JsonValue"/>, otherwise null. Mirrors the former
    /// <c>node is JsonValue v &amp;&amp; v.TryGetValue&lt;string&gt;(out var s) ? s : null</c>.
    /// </summary>
    public static string? AsString(this JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
