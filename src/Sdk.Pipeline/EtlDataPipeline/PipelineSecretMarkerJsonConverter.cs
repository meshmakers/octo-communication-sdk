using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// The pipeline's <see cref="RtSecretValue" /> converter (AB#5538). It is registered in the options
/// (<see cref="SystemTextJsonOptions.CreateDefault" />), so it takes precedence over the engine's
/// type-level converter for every value written into or read from a data context.
/// </summary>
/// <remarks>
/// <para>
/// Writing: with a registered classifier (<see cref="PipelineSecretValues.SetReadStateClassifier" />) the
/// marker reflects the read state: <c>{"isSet": true|false}</c>, or <c>{"isSet": false, "keyMissing": true}</c>
/// for a stored value whose key id is not in the key ring. Without a classifier it writes the engine
/// marker (<see cref="RtSecretValueWireFormat.Write(Utf8JsonWriter, RtSecretValue)" />). Never the
/// plaintext, never the envelope.
/// </para>
/// <para>
/// Reading is the engine's strict wire format (<see cref="RtSecretValueWireFormat.Read(ref Utf8JsonReader)" />):
/// any marker, including one with <c>keyMissing</c> / <c>setAt</c>, means "unchanged".
/// </para>
/// </remarks>
public sealed class PipelineSecretMarkerJsonConverter : JsonConverter<RtSecretValue>
{
    /// <inheritdoc />
    public override bool HandleNull => false;

    /// <inheritdoc />
    public override RtSecretValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return RtSecretValueWireFormat.Read(ref reader);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RtSecretValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        var classifier = PipelineSecretValues.ReadStateClassifier;
        if (classifier == null)
        {
            RtSecretValueWireFormat.Write(writer, value);
            return;
        }

        var info = classifier(value);
        writer.WriteStartObject();
        writer.WriteBoolean(RtSecretValueWireFormat.IsSetPropertyName, info.IsSet);
        if (info.KeyMissing)
        {
            writer.WriteBoolean(RtSecretValueWireFormat.KeyMissingPropertyName, true);
        }

        writer.WriteEndObject();
    }
}
