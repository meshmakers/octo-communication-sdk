using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
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
/// With a registered read-state classifier (<see cref="SetReadStateClassifier" />, the mesh adapter
/// registers its key ring) a stored value whose key id is unknown reads as
/// <c>{"isSet": false, "keyMissing": true}</c>. Echoing any marker back into a write means "unchanged".
/// </para>
/// <para>
/// Reading the marker's <c>isSet</c> property (<c>$.attributes.password.isSet</c> as Boolean) stays
/// allowed — that is how a pipeline asks whether a secret is configured.
/// </para>
/// </remarks>
public static class PipelineSecretValues
{
    private static Func<RtSecretValue, SecretReadInfo>? _readStateClassifier;

    /// <summary>
    /// The process-wide classifier the pipeline serialiser uses to write a Secret marker that reflects
    /// the read state (AB#5538); <c>null</c> when none is registered.
    /// </summary>
    public static Func<RtSecretValue, SecretReadInfo>? ReadStateClassifier => Volatile.Read(ref _readStateClassifier);

    /// <summary>
    /// Registers the classifier the pipeline serialiser (<see cref="SystemTextJsonOptions" />) uses when it
    /// writes a Secret value into the data context. A host with a key ring (the mesh adapter) passes
    /// <c>ISecretAttributeProtector.DescribeSecret</c>, so a stored value whose key id is not in the ring is
    /// written as <c>{"isSet": false, "keyMissing": true}</c> instead of the key-ring-less
    /// <c>{"isSet": true}</c> of <c>RtSecretValueWireFormat</c>. Without a classifier the engine marker is
    /// written unchanged.
    /// </summary>
    /// <remarks>
    /// The key ring is per process (the protector is a singleton), hence a process-wide registration:
    /// the serialiser options are static and shared by every data context.
    /// </remarks>
    /// <param name="classifier">The classifier, or <c>null</c> to remove it</param>
    public static void SetReadStateClassifier(Func<RtSecretValue, SecretReadInfo>? classifier)
    {
        Volatile.Write(ref _readStateClassifier, classifier);
    }

    /// <summary>
    /// Removes <paramref name="classifier" /> if it is still the registered one (a later registration by
    /// another host in the same process is kept).
    /// </summary>
    /// <param name="classifier">The classifier registered before</param>
    public static void ResetReadStateClassifier(Func<RtSecretValue, SecretReadInfo> classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        Interlocked.CompareExchange(ref _readStateClassifier, null, classifier);
    }

    /// <summary>
    /// True when <paramref name="node" /> is a Secret marker: an object with the boolean property
    /// <c>isSet</c> and otherwise only the optional properties <c>keyMissing</c> (boolean) and
    /// <c>setAt</c> (string or null). Property names are matched case-sensitively, as they are written.
    /// </summary>
    public static bool IsSecretMarker(JsonNode? node)
    {
        if (node is not JsonObject obj || !obj.ContainsKey(RtSecretValueWireFormat.IsSetPropertyName))
        {
            return false;
        }

        foreach (var (key, value) in obj)
        {
            var valid = key switch
            {
                RtSecretValueWireFormat.IsSetPropertyName or RtSecretValueWireFormat.KeyMissingPropertyName =>
                    value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
                RtSecretValueWireFormat.SetAtPropertyName =>
                    value is null || (value is JsonValue d && d.GetValueKind() == JsonValueKind.String),
                _ => false
            };
            if (!valid)
            {
                return false;
            }
        }

        return true;
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
