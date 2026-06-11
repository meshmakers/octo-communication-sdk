using System.Collections.Generic;
using System.Text.Json;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// A read-only structural view over a JSON value, abstracting the concrete backing
/// representation (<see cref="JsonElement"/> or <see cref="System.Text.Json.Nodes.JsonNode"/>).
/// Implemented by struct value types so that <see cref="JsonPathWalker"/> can walk either
/// representation without boxing.
/// </summary>
/// <typeparam name="TSelf">The implementing struct type (CRTP self-reference).</typeparam>
internal interface IJsonView<TSelf> where TSelf : struct, IJsonView<TSelf>
{
    /// <summary>The JSON value kind of this node.</summary>
    JsonValueKind Kind { get; }

    /// <summary>Tries to get a child by object property name.</summary>
    /// <param name="name">Property name to look up.</param>
    /// <param name="child">The child view if found.</param>
    /// <returns>True if this is an object with the named property.</returns>
    bool TryGetProperty(string name, out TSelf child);

    /// <summary>Tries to get a child by array index.</summary>
    /// <param name="index">Zero-based array index.</param>
    /// <param name="child">The child view if in range.</param>
    /// <returns>True if this is an array and <paramref name="index"/> is in range.</returns>
    bool TryGetIndex(int index, out TSelf child);

    /// <summary>Number of elements when this is an array; otherwise 0.</summary>
    int ArrayLength { get; }

    /// <summary>
    /// Enumerates the direct children. For array items the <c>Key</c> is the empty string;
    /// for object members the <c>Key</c> is the property name.
    /// </summary>
    IEnumerable<(string Key, TSelf Value)> EnumerateChildren();

    /// <summary>Returns the raw JSON text of this node.</summary>
    string GetRawText();

    /// <summary>Tries to read this node as a string scalar.</summary>
    /// <param name="value">The string value if this is a JSON string.</param>
    /// <returns>True if this node is a JSON string.</returns>
    bool TryGetString(out string value);
}
