using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// A single <c>IReadSource.Evaluate</c> match, detached from the source so it survives the
/// source's (and the parent context's) disposal. Carries EITHER an owned <see cref="JsonElement"/>
/// (the element fast path, via <see cref="JsonElement.Clone"/>) OR an orphan <see cref="JsonNode"/>
/// (the node / lifted path, via <see cref="JsonNode.DeepClone"/>) — never a UTF-16 JSON string.
/// </summary>
/// <remarks>
/// The <see cref="IsNode"/> / null split is load-bearing for the explicit-null-vs-undefined
/// distinction: a JSON-null match is <see cref="FromNode"/> with a <c>null</c> node — present,
/// kind <c>Null</c> — and is distinct from "no match" (the enumerable simply yields nothing).
/// </remarks>
internal readonly struct DetachedMatch
{
    public string CanonicalPath { get; }

    /// <summary>The owned element, set on the element fast path; <c>null</c> on the node path.</summary>
    public JsonElement? Element { get; }

    /// <summary>The orphan node, set on the node/lifted path; may itself be <c>null</c> (JSON null).</summary>
    public JsonNode? Node { get; }

    /// <summary>
    /// True when this match should be wrapped as a node-backed context. Node-path matches
    /// (including a present-but-null JSON null, where <see cref="Node"/> is <c>null</c> but
    /// <see cref="Element"/> is also <c>null</c>) are node-backed; element-path matches are not.
    /// </summary>
    public bool IsNode => Node is not null || Element is null;

    private DetachedMatch(string path, JsonElement? element, JsonNode? node)
    {
        CanonicalPath = path;
        Element = element;
        Node = node;
    }

    public static DetachedMatch FromElement(string canonicalPath, JsonElement element) =>
        new(canonicalPath, element, null);

    public static DetachedMatch FromNode(string canonicalPath, JsonNode? node) =>
        new(canonicalPath, null, node);
}
