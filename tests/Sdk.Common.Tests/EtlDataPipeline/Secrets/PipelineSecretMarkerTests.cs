using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538: the Secret marker written into the data context reflects the read state of the host's key
/// ring when a classifier is registered.
/// </summary>
/// <remarks>
/// The classifier registration is process-wide; this class is not run in parallel with itself and its
/// classifier only changes the result for the key id <see cref="UnknownKid" />, which no other test uses.
/// </remarks>
[Collection(nameof(PipelineSecretMarkerTests))]
public class PipelineSecretMarkerTests
{
    private const string UnknownKid = "unknown-kid-5538";
    private const string KnownKid = "known-kid-5538";

    private static readonly Func<RtSecretValue, SecretReadInfo> Classifier =
        value => SecretValueStates.Describe(value, kid => kid != UnknownKid);

    // Structurally valid enc:v2 envelope (nonce + tag + 4 bytes ciphertext, all zero) - not decryptable.
    private static RtSecretValue Envelope(string kid) =>
        RtSecretValue.Protected($"enc:v2:{kid}:{Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_')}");

    private static JsonNode? SetAndRead(object value)
    {
        var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        dataContext.Set("$.entity", value);
        return dataContext.Get<JsonNode>("$.entity");
    }

    private static RtEntity Entity(RtSecretValue? password)
    {
        var entity = new RtEntity();
        entity.SetAttributeRawValue("password", password);
        entity.SetAttributeRawValue("name", "svc");
        return entity;
    }

    [Fact]
    public void WithoutClassifier_WritesEngineMarker()
    {
        var node = SetAndRead(Entity(Envelope(UnknownKid)));

        Assert.Equal("""{"isSet":true}""", node!["Attributes"]!["password"]!.ToJsonString());
    }

    [Fact]
    public void WithClassifier_UnknownKid_WritesKeyMissingMarker()
    {
        PipelineSecretValues.SetReadStateClassifier(Classifier);
        try
        {
            var node = SetAndRead(Entity(Envelope(UnknownKid)));

            var marker = node!["Attributes"]!["password"]!;
            Assert.Equal("""{"isSet":false,"keyMissing":true}""", marker.ToJsonString());
            Assert.True(PipelineSecretValues.IsSecretMarker(marker));
        }
        finally
        {
            PipelineSecretValues.ResetReadStateClassifier(Classifier);
        }
    }

    [Fact]
    public void WithClassifier_KnownKidAndNotSet_WriteIsSetOnly()
    {
        PipelineSecretValues.SetReadStateClassifier(Classifier);
        try
        {
            Assert.Equal("""{"isSet":true}""",
                SetAndRead(Entity(Envelope(KnownKid)))!["Attributes"]!["password"]!.ToJsonString());
            Assert.Equal("""{"isSet":false}""",
                SetAndRead(Entity(RtSecretValue.LegacyPlaintext(string.Empty)))!["Attributes"]!["password"]!.ToJsonString());
        }
        finally
        {
            PipelineSecretValues.ResetReadStateClassifier(Classifier);
        }
    }

    [Fact]
    public void WithClassifier_RecordMember_WritesKeyMissingMarker()
    {
        PipelineSecretValues.SetReadStateClassifier(Classifier);
        try
        {
            var record = new RtRecord();
            record.SetAttributeRawValue("token", Envelope(UnknownKid));
            var entity = new RtEntity();
            entity.SetAttributeRawValue("credentials", new List<object> { record });

            var node = SetAndRead(entity);

            Assert.Equal("""{"isSet":false,"keyMissing":true}""",
                node!["Attributes"]!["credentials"]![0]!["Attributes"]!["token"]!.ToJsonString());
        }
        finally
        {
            PipelineSecretValues.ResetReadStateClassifier(Classifier);
        }
    }

    [Theory]
    [InlineData("""{"isSet":false,"keyMissing":true}""")]
    [InlineData("""{"isSet":true,"setAt":"2026-10-06T08:00:00Z"}""")]
    [InlineData("""{"isSet":true}""")]
    public void KeyMissingMarker_ReadsBackAsUnchanged(string marker)
    {
        var value = JsonSerializer.Deserialize<RtSecretValue>(marker, SystemTextJsonOptions.Default);

        Assert.NotNull(value);
        Assert.True(value.IsPending);
        // An empty pending value is "unchanged" for the write normaliser (not a new secret).
        Assert.Equal(SecretValueState.NotSet, SecretValueStates.GetReadState(value, (Func<string?, bool>?)null));
    }

    [Fact]
    public void ResetReadStateClassifier_KeepsAForeignRegistration()
    {
        Func<RtSecretValue, SecretReadInfo> other = value => SecretValueStates.Describe(value, kid => kid != UnknownKid);
        PipelineSecretValues.SetReadStateClassifier(other);
        try
        {
            PipelineSecretValues.ResetReadStateClassifier(Classifier);

            Assert.Same(other, PipelineSecretValues.ReadStateClassifier);
        }
        finally
        {
            PipelineSecretValues.ResetReadStateClassifier(other);
        }
    }
}
