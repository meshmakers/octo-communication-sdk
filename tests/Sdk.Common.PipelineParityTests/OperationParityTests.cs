using System.Text;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Oracle-backed parity for the multi-match read/write and read-after-<c>Set</c> operations the
/// production adapters actually depend on — the gap the coverage audit flagged: the original
/// <see cref="WriteParityTests"/> covered only three trivial standalone <c>Set</c> cases, while
/// <c>SelectMatches</c> ordering, <c>UpdateMatchesAsync</c> multi-match writes, read-after-<c>Set</c>
/// ordering, last-match-wins, and append/overwrite interleaving were pinned only by STJ-vs-STJ unit
/// tests (or by node tests that mock the context). Here Newtonsoft 13.x is the oracle: the STJ
/// <see cref="DataContextImpl"/> result must match the equivalent <see cref="JToken"/> mutation /
/// selection.
///
/// <para>
/// Values are kept integral or string so the simple parse-then-STJ-serialize
/// <see cref="Canonicalize"/> (whitespace-only normalization) is sufficient; trailing-zero number
/// normalization (see <see cref="ReadParityTests"/>) and non-ASCII encoding parity are out of scope
/// for this file by design (encoding parity is covered separately).
/// </para>
/// </summary>
public class OperationParityTests
{
    // ---- helpers -------------------------------------------------------

    /// <summary>Whitespace-canonical JSON of the whole STJ context document.</summary>
    private static string StjDoc(DataContextImpl ctx)
    {
        using var ms = new MemoryStream();
        ctx.WriteJsonTo("$", ms);
        return Canonicalize(Encoding.UTF8.GetString(ms.ToArray()));
    }

    /// <summary>Whitespace-canonical JSON of a single SelectMatches / sub-context match.</summary>
    private static string StjMatch(IDataContext match)
    {
        using var ms = new MemoryStream();
        match.WriteJsonTo("$", ms);
        return Canonicalize(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static string Nso(JToken token) => ParityJson.Nso(token);

    private static string Canonicalize(string json) => ParityJson.Canonicalize(json);

    // ---- SelectMatches: document-order preservation (last-match-wins dependency) -------

    [Fact]
    public void SelectMatches_PreservesDocumentOrder_LikeSelectTokens()
    {
        const string json = """{"items":[{"k":"a"},{"k":"b"},{"k":"c"}]}""";

        var jt = JToken.Parse(json);
        var expected = jt.SelectTokens("$.items[*].k").Select(Nso).ToList();

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        var actual = ctx.SelectMatches("$.items[*].k").Select(StjMatch).ToList();

        // ORDERED equality — adapters (e.g. CreateUpdateInfoNode) depend on last-match-wins, which
        // requires SelectMatches to enumerate in the same document order as Newtonsoft SelectTokens.
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SelectMatches_LastMatchWins_AgreesWithNewtonsoft()
    {
        const string json = """{"events":[{"ts":"a"},{"ts":"b"},{"ts":"c"}]}""";

        var jt = JToken.Parse(json);
        var expectedLast = Nso(jt.SelectTokens("$.events[*].ts").Last());

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        var actualLast = StjMatch(ctx.SelectMatches("$.events[*].ts").Last());

        Assert.Equal("\"c\"", actualLast); // pins it is the document-order LAST, not just "equal"
        Assert.Equal(expectedLast, actualLast);
    }

    // ---- read-after-Set ------------------------------------------------

    [Fact]
    public void SetThenSelectMatches_SeesUpdatedValue_LikeNewtonsoftMutateThenSelect()
    {
        const string json = """{"items":[{"v":1},{"v":2}]}""";

        var jt = JToken.Parse(json);
        jt["items"]![0]!["v"] = 99;
        var expected = jt.SelectTokens("$.items[*].v").Select(Nso).ToList();

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.items[0].v", 99);
        var actual = ctx.SelectMatches("$.items[*].v").Select(StjMatch).ToList();

        Assert.Equal(expected, actual); // [99, 2]
    }

    [Fact]
    public void SetNewProperty_ThenSerialize_MatchesNewtonsoft()
    {
        const string json = """{"items":[{"v":1}]}""";

        var jt = JObject.Parse(json);
        ((JObject)jt["items"]![0]!)["w"] = "new";
        var expected = Nso(jt);

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.items[0].w", "new");

        Assert.Equal(expected, StjDoc(ctx)); // {"items":[{"v":1,"w":"new"}]}
    }

    // ---- UpdateMatchesAsync: multi-match writes ------------------------

    [Fact]
    public async Task UpdateMatchesAsync_OverwriteEachMatch_MatchesNewtonsoft()
    {
        const string json = """{"items":[{"v":1},{"v":2},{"v":3}]}""";

        var jt = JObject.Parse(json);
        foreach (var token in jt.SelectTokens("$.items[*]"))
        {
            token["v"] = (int)token["v"]! * 10;
        }
        var expected = Nso(jt);

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        await ctx.UpdateMatchesAsync("$.items[*]", sub =>
        {
            var v = sub.Get<int>("$.v");
            sub.Set("$.v", v * 10);
            return Task.CompletedTask;
        });

        Assert.Equal(expected, StjDoc(ctx)); // {"items":[{"v":10},{"v":20},{"v":30}]}
    }

    [Fact]
    public async Task UpdateMatchesAsync_AddNewPropertyToEachMatch_MatchesNewtonsoft()
    {
        const string json = """{"items":[{"id":1},{"id":2}]}""";

        var jt = JObject.Parse(json);
        foreach (var token in jt.SelectTokens("$.items[*]"))
        {
            token["doubled"] = (int)token["id"]! * 2;
        }
        var expected = Nso(jt);

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        await ctx.UpdateMatchesAsync("$.items[*]", sub =>
        {
            sub.Set("$.doubled", sub.Get<int>("$.id") * 2);
            return Task.CompletedTask;
        });

        Assert.Equal(expected, StjDoc(ctx)); // {"items":[{"id":1,"doubled":2},{"id":2,"doubled":4}]}
    }

    // ---- append / overwrite interleaving -------------------------------

    [Fact]
    public void AppendTwiceThenOverwriteIndex_MatchesNewtonsoft()
    {
        const string json = """{"arr":[1,2]}""";

        var jt = JObject.Parse(json);
        var arr = (JArray)jt["arr"]!;
        arr.Add(3);
        arr.Add(4);
        arr[0] = 99;
        var expected = Nso(jt);

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.arr", 3, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);
        ctx.Set("$.arr", 4, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);
        ctx.Set("$.arr[0]", 99);

        Assert.Equal(expected, StjDoc(ctx)); // {"arr":[99,2,3,4]}
    }
}
