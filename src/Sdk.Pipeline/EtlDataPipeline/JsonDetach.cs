using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// The cheap detach / element↔node bridge primitive the System.Text.Json two-type model lacks.
/// Replaces the <c>ToJsonString()</c>/<c>GetRawText()</c> + <c>JsonDocument.Parse</c> round-trips
/// scattered through the pipeline read seam: detaching a match no longer serialises to a UTF-16
/// string and re-parses it.
/// </summary>
/// <remarks>
/// Numeric/Newtonsoft parity is preserved structurally: <see cref="JsonElement.Clone"/> and
/// <see cref="JsonNode.DeepClone"/> copy the raw JSON token verbatim and touch no converters; the
/// <c>NewtonsoftParity*</c> converters fire only at the typed <c>Get&lt;T&gt;</c>/serialize step,
/// never at detach. The element→node bridge routes through <see cref="SystemTextJsonOptions.Default"/>
/// (the pipeline bundle, which PRESERVES nulls) so null/converter semantics match the read side —
/// never <c>RtSystemTextJsonSerializer.Default</c>, which drops nulls.
/// </remarks>
internal static class JsonDetach
{
    /// <summary>
    /// Detach an element-view match into a self-owned <see cref="JsonElement"/> via
    /// <see cref="JsonElement.Clone"/> — survives the source <see cref="JsonDocument"/> being
    /// disposed; no string, no transcode, no reparse.
    /// </summary>
    public static DetachedMatch Detach(in ElementView view, string canonicalPath) =>
        DetachedMatch.FromElement(canonicalPath, view.Element.Clone());

    /// <summary>
    /// Detach a node-view match into an orphan <see cref="JsonNode"/> via
    /// <see cref="JsonNode.DeepClone"/>. A <c>null</c> node (JSON null) detaches to <c>null</c> —
    /// present-but-null is preserved.
    /// </summary>
    public static DetachedMatch Detach(in NodeView view, string canonicalPath) =>
        DetachedMatch.FromNode(canonicalPath, view.Node?.DeepClone());

    /// <summary>
    /// Bridge a <see cref="JsonElement"/> to a detached <see cref="JsonNode"/> WITHOUT a UTF-16
    /// string — replaces every <c>JsonNode.Parse(element.GetRawText())</c>. A JSON-null element
    /// returns <c>null</c> (present-but-null preserved).
    /// </summary>
    /// <remarks>
    /// Built via <see cref="SystemTextJsonOptions.NodeNavigation"/> (NOT <c>Default</c>), so the
    /// resulting <c>JsonObject</c> tree navigates property names CASE-SENSITIVELY — matching
    /// Newtonsoft's <c>JObject</c>/<c>SelectToken</c> and the always-ordinal <c>JsonElement</c> base
    /// reads, and resolving the lifted-overlay vs element-base inconsistency. This is the single
    /// element→node chokepoint (it backs <c>DataOverlay.EnsureLifted</c>, the alias materialization in
    /// <c>LayeredSource</c>, ancestor seeding, and <c>FoldAliases</c>), and <c>JsonNode.DeepClone</c>
    /// preserves the options, so every downstream clone inherits case-sensitive navigation. Typed
    /// <c>Deserialize&lt;T&gt;</c> (e.g. <c>DataContextImpl.Get&lt;T&gt;</c>) deliberately stays on the
    /// case-insensitive <c>Default</c> bundle. Case sensitivity is the ONLY difference from
    /// <c>Default</c>; converters, encoder, number handling, and null preservation are identical, so
    /// numeric/null/encoding parity at the bridge is unchanged.
    /// </remarks>
    public static JsonNode? ToNode(in JsonElement element) =>
        element.Deserialize<JsonNode>(SystemTextJsonOptions.NodeNavigation);

    /// <summary>
    /// Materialise a node into an owned <see cref="JsonDocument"/> without a
    /// <c>ToJsonString()</c> + <c>Parse</c> hop (backs <c>Select</c> and standalone wrapping).
    /// </summary>
    public static JsonDocument ToDocument(JsonNode? node) =>
        JsonSerializer.SerializeToDocument(node, SystemTextJsonOptions.Default);
}
