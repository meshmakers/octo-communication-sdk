using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Case-sensitivity parity. Exact Newtonsoft behavior is SPLIT:
/// <list type="bullet">
/// <item>document-model NAVIGATION (JObject indexer / SelectToken / JSONPath) is CASE-SENSITIVE;</item>
/// <item>typed CLR mapping (ToObject&lt;T&gt;) is CASE-INSENSITIVE.</item>
/// </list>
/// The STJ pipeline must match both. Regression for the handle-ec-podlist-process.yaml failure:
/// "Cannot set '.UpdateProcess' on non-object" — "$.Updates" (capital) collided with a sibling
/// "$.updates" (lowercase) array because the LIFTED overlay JsonObject matched keys
/// case-insensitively (inherited PropertyNameCaseInsensitive=true), unlike Newtonsoft's JObject.
/// </summary>
public class CaseSensitivityParityTests
{
    // Only lowercase keys present; capital-cased reads must MISS (Newtonsoft parity).
    private const string LowerOnly = "{\"updates\":[1,2],\"nested\":{\"value\":20}}";

    public static IEnumerable<object[]> NavigationPaths()
    {
        yield return new object[] { "$.updates" };      // exact      -> present
        yield return new object[] { "$.Updates" };      // capital    -> ABSENT (bug returned Array)
        yield return new object[] { "$.UPDATES" };      // all-caps   -> ABSENT
        yield return new object[] { "$.nested.value" }; // exact      -> present
        yield return new object[] { "$.nested.Value" }; // capital    -> ABSENT (bug returned Number)
        yield return new object[] { "$.missing" };      // absent     -> absent
    }

    [Theory]
    [MemberData(nameof(NavigationPaths))]
    public void Navigation_AfterLift_MatchesNewtonsoftCaseSensitivity(string path)
    {
        // Newtonsoft oracle: JObject / SelectToken navigation is case-sensitive.
        var expectedPresent = JToken.Parse(LowerOnly).SelectToken(path) is not null;

        // STJ DataContext, forced lift so navigation routes through the JsonNode overlay tree
        // (the case-insensitive seam) rather than the always-ordinal JsonElement base.
        using var doc = JsonDocument.Parse(LowerOnly);
        var dc = new DataContextImpl(doc);
        dc.Set("$.__forceLift", JsonValue.Create(1));

        var actualPresent = dc.GetKind(path) != DataKind.Undefined;

        Assert.Equal(expectedPresent, actualPresent);
    }

    [Fact]
    public void Navigation_BeforeAndAfterLift_AreConsistent()
    {
        // The same path must resolve identically whether or not a prior write lifted the overlay.
        using var doc = JsonDocument.Parse(LowerOnly);
        var dc = new DataContextImpl(doc);

        var beforeLift = dc.GetKind("$.Updates"); // element base — already case-sensitive (Undefined)
        dc.Set("$.__forceLift", JsonValue.Create(1));
        var afterLift = dc.GetKind("$.Updates");  // node overlay — must agree

        Assert.Equal(DataKind.Undefined, beforeLift);
        Assert.Equal(beforeLift, afterLift);
    }

    [Fact]
    public void SiblingCaseCollision_WriteCapitalPath_CreatesDistinctKey()
    {
        // Mirrors the pipeline: a sibling ForEach targetPath "$.updates" (lowercase) left an array;
        // a CreateUpdateInfo write targets "$.Updates.UpdateProcess" (capital). In Newtonsoft these
        // are distinct keys, so the write creates a new "$.Updates" object. Under case-insensitive
        // STJ matching, "$.Updates" collided with the "updates" array -> "Cannot set on non-object".
        using var doc = JsonDocument.Parse("{\"updates\":[{\"a\":1}]}");
        var dc = new DataContextImpl(doc);

        dc.Set("$.Updates.UpdateProcess", JsonValue.Create(true));

        Assert.Equal(DataKind.Array, dc.GetKind("$.updates"));  // untouched
        Assert.Equal(DataKind.Object, dc.GetKind("$.Updates")); // new distinct object
        Assert.True(dc.Get<bool>("$.Updates.UpdateProcess"));
    }

    private sealed class CaseDto
    {
        public int[]? Updates { get; set; }
    }

    [Fact]
    public void TypedGet_StaysCaseInsensitive_LikeNewtonsoftToObject()
    {
        // Split-guard: typed CLR mapping must remain CASE-INSENSITIVE (Newtonsoft ToObject<T> is),
        // so an accidental global PropertyNameCaseInsensitive=false flip — which would null out the
        // camelCase-ctor CK-engine types (EntityUpdateInfo/RtEntity/RtRecord) in ApplyChanges — is
        // caught here. This must stay GREEN.
        const string json = "{\"updates\":[1,2]}";

        var newtonsoft = JObject.Parse(json).ToObject<CaseDto>();
        Assert.Equal(new[] { 1, 2 }, newtonsoft!.Updates);

        using var doc = JsonDocument.Parse(json);
        var dc = new DataContextImpl(doc);
        var stj = dc.Get<CaseDto>("$");
        Assert.Equal(new[] { 1, 2 }, stj!.Updates);
    }
}
