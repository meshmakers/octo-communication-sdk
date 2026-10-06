using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// How a Secret attribute value shows up in the data context, and the guard that type-switch nodes
/// (<c>ConvertDataType@1</c>, <c>SetPrimitiveValue@1</c>, <c>If@1</c>, <c>Switch@1</c>,
/// <c>ExecuteCSharp@1</c>, the adapter's <c>DataMapping@1</c>) use to refuse it explicitly (AB#5538).
/// </summary>
/// <remarks>
/// <para>
/// Entities enter the data context through the engine serialisers, which write a Secret value only as
/// the marker <c>{"isSet": true|false}</c> (<c>RtSecretValueWireFormat</c>) — never the plaintext and
/// never the envelope. A node therefore sees a Secret in one of two ways: as that marker object at the
/// path it reads, or as the configured value type <see cref="AttributeValueTypesDto.Secret" />. Before
/// AB#5538 the first surfaced as a generic "value is an object" / JSON conversion error and the second
/// fell into each node's default branch; both now raise
/// <see cref="PipelineExecutionException.SecretNotSupported" />.
/// </para>
/// <para>
/// Reading the marker's <c>isSet</c> property (<c>$.attributes.password.isSet</c> as Boolean) stays
/// allowed — that is how a pipeline asks whether a secret is configured.
/// </para>
/// </remarks>
public static class PipelineSecretValues
{
    /// <summary>
    /// True when <paramref name="node" /> is a Secret marker: an object whose only property is
    /// <c>isSet</c> with a boolean value.
    /// </summary>
    public static bool IsSecretMarker(JsonNode? node)
    {
        if (node is not JsonObject obj || obj.Count != 1)
        {
            return false;
        }

        var property = obj.First();
        return string.Equals(property.Key, RtSecretValueWireFormat.IsSetPropertyName, StringComparison.Ordinal) &&
               property.Value is JsonValue value &&
               value.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False;
    }

    /// <summary>
    /// True when the value at <paramref name="path" /> is a Secret marker (see
    /// <see cref="IsSecretMarker(JsonNode?)" />). Cheap for anything that is not an object.
    /// </summary>
    public static bool IsSecretMarker(IDataContext dataContext, string? path)
    {
        ArgumentNullException.ThrowIfNull(dataContext);
        if (string.IsNullOrEmpty(path) || dataContext.GetKind(path) != DataKind.Object)
        {
            return false;
        }

        return IsSecretMarker(dataContext.Get<JsonNode>(path));
    }

    /// <summary>
    /// Throws <see cref="PipelineExecutionException.SecretNotSupported" /> when
    /// <paramref name="valueType" /> is <see cref="AttributeValueTypesDto.Secret" />.
    /// </summary>
    /// <param name="nodeContext">The node context, for the node path in the message</param>
    /// <param name="valueType">The configured value type</param>
    /// <param name="settingName">Name of the setting carrying the type, for the message</param>
    public static void ThrowIfSecretValueType(INodeContext nodeContext, AttributeValueTypesDto? valueType,
        string settingName)
    {
        ArgumentNullException.ThrowIfNull(nodeContext);
        if (valueType == AttributeValueTypesDto.Secret)
        {
            throw PipelineExecutionException.SecretNotSupported(nodeContext.NodePath, settingName);
        }
    }

    /// <summary>
    /// Throws <see cref="PipelineExecutionException.SecretNotSupported" /> when one of
    /// <paramref name="paths" /> holds a Secret marker. Null or empty paths are skipped.
    /// </summary>
    /// <param name="nodeContext">The node context, for the node path in the message</param>
    /// <param name="dataContext">The data context</param>
    /// <param name="paths">The data paths the node reads</param>
    public static void ThrowIfSecretMarker(INodeContext nodeContext, IDataContext dataContext,
        params string?[] paths)
    {
        ArgumentNullException.ThrowIfNull(nodeContext);
        foreach (var path in paths)
        {
            if (IsSecretMarker(dataContext, path))
            {
                throw PipelineExecutionException.SecretNotSupported(nodeContext.NodePath, path);
            }
        }
    }
}
