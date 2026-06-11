using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Newtonsoft.Json;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Targeted wire-format assertions for custom CK / runtime identifier types. The corpus-driven
/// CLR-type round-trip tests cover that an attribute-dict slot survives serialization, but they
/// cannot catch on-wire string regressions for types that intentionally collapse to a JSON scalar
/// (RtCkId, OctoObjectId, OctoObjectId[]) — both sides round-trip to the same CLR type even when
/// the emitted strings drift apart (e.g. <c>FullName</c> vs <c>SemanticVersionedFullName</c>).
/// These tests pin the canonical wire form directly.
/// </summary>
public class CustomTypeWireFormatTests
{
    private static readonly JsonSerializerOptions StjOptions = RtSystemTextJsonSerializer.Default;
    private static readonly Newtonsoft.Json.JsonSerializer NewtonsoftSerializer = RtNewtonsoftSerializer.DefaultSerializer;

    /// <summary>
    /// RtCkId&lt;CkTypeId&gt; must serialize to its <c>SemanticVersionedFullName</c> string (the
    /// form Mongo BSON stores, the legacy Newtonsoft <c>ToString()</c> output, and what
    /// <c>CkTypeIdHelper</c> reconstructs from). Catches a <c>FullName</c> regression in
    /// <c>RtCkIdConverter.Write</c>.
    /// </summary>
    [Fact]
    public void Stj_RtCkIdTypeId_v1_WritesSemanticVersionedFullName()
    {
        var rtCkId = new RtCkId<CkTypeId>("Demo/Test");
        var json = System.Text.Json.JsonSerializer.Serialize(rtCkId, StjOptions);
        Assert.Equal("\"Demo/Test\"", json);
    }

    [Fact]
    public void Stj_RtCkIdTypeId_v2_WritesSemanticVersionedFullName()
    {
        var rtCkId = new RtCkId<CkTypeId>("Demo/Test-2");
        var json = System.Text.Json.JsonSerializer.Serialize(rtCkId, StjOptions);
        Assert.Equal("\"Demo/Test-2\"", json);
    }

    [Fact]
    public void Stj_RtCkIdRecordId_WritesSemanticVersionedFullName()
    {
        var rtCkId = new RtCkId<CkRecordId>("Basic/Contact");
        var json = System.Text.Json.JsonSerializer.Serialize(rtCkId, StjOptions);
        Assert.Equal("\"Basic/Contact\"", json);
    }

    /// <summary>
    /// Newtonsoft writes <c>rtCkId.ToString()</c>, which equals <c>SemanticVersionedFullName</c>.
    /// Pins parity between the two serializers — drift triggers a wire-format regression.
    /// </summary>
    [Fact]
    public void NewtonsoftAndStj_RtCkIdTypeId_AgreeOnWire()
    {
        var rtCkId = new RtCkId<CkTypeId>("Demo/Test");
        var stj = System.Text.Json.JsonSerializer.Serialize(rtCkId, StjOptions);
        var newtonsoft = SerializeNewtonsoft(rtCkId);
        Assert.Equal(newtonsoft, stj);
    }

    [Fact]
    public void NewtonsoftAndStj_RtCkIdRecordId_AgreeOnWire()
    {
        var rtCkId = new RtCkId<CkRecordId>("Basic/Contact");
        var stj = System.Text.Json.JsonSerializer.Serialize(rtCkId, StjOptions);
        var newtonsoft = SerializeNewtonsoft(rtCkId);
        Assert.Equal(newtonsoft, stj);
    }

    /// <summary>
    /// OctoObjectId arrays must serialize element-wise to the 24-hex-char ObjectId string.
    /// Pins against the copy-paste regression where the loop emitted the array's
    /// <c>ToString()</c> (the type name) instead of each element's hex.
    /// </summary>
    [Fact]
    public void Stj_OctoObjectIdArray_WritesHexStrings()
    {
        var ids = new[]
        {
            new OctoObjectId("66004fda527ac79a03ecedd7"),
            new OctoObjectId("66004fda527ac79a03ecedd8")
        };
        var json = System.Text.Json.JsonSerializer.Serialize(ids, StjOptions);
        Assert.Equal("[\"66004fda527ac79a03ecedd7\",\"66004fda527ac79a03ecedd8\"]", json);
        Assert.DoesNotContain("OctoObjectId[]", json);
    }

    [Fact]
    public void Stj_OctoObjectIdArray_SingleElement_WritesHexString()
    {
        var ids = new[] { new OctoObjectId("66004fda527ac79a03ecedd7") };
        var json = System.Text.Json.JsonSerializer.Serialize(ids, StjOptions);
        Assert.Equal("[\"66004fda527ac79a03ecedd7\"]", json);
    }

    /// <summary>
    /// Scalar <c>OctoObjectId</c> writes the hex string — sanity check for the converter that
    /// the array converter is delegating to (and parity check against the Newtonsoft converter).
    /// </summary>
    [Fact]
    public void Stj_OctoObjectId_WritesHexString()
    {
        var id = new OctoObjectId("66004fda527ac79a03ecedd7");
        var json = System.Text.Json.JsonSerializer.Serialize(id, StjOptions);
        Assert.Equal("\"66004fda527ac79a03ecedd7\"", json);
    }

    private enum SampleDirection
    {
        Unknown = 0,
        Consumption = 1,
        Production = 2
    }

    /// <summary>
    /// A plain CLR enum must serialize to its underlying INTEGER, matching Newtonsoft (which has no
    /// StringEnumConverter). Guards the <c>NewtonsoftParityEnumConverter</c> against a regression back
    /// to name emission — the bug that broke pipeline consumers reading enums as Int (DataMapping@1 /
    /// If@1 / Switch@1). The corpus-driven tests have no enum case, so this pins it directly.
    /// </summary>
    [Fact]
    public void Stj_ClrEnum_WritesUnderlyingInteger()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(SampleDirection.Production, StjOptions);
        Assert.Equal("2", json);
    }

    [Fact]
    public void NewtonsoftAndStj_ClrEnum_AgreeOnWire()
    {
        var stj = System.Text.Json.JsonSerializer.Serialize(SampleDirection.Production, StjOptions);
        var newtonsoft = SerializeNewtonsoft(SampleDirection.Production);
        Assert.Equal(newtonsoft, stj);
    }

    private static string SerializeNewtonsoft<T>(T value)
    {
        using var sw = new System.IO.StringWriter();
        using (var jw = new JsonTextWriter(sw))
        {
            NewtonsoftSerializer.Serialize(jw, value);
        }
        return sw.ToString();
    }
}
