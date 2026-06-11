using System.Text;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Predicate-exactness matrix for the overlay read-after-<c>Set</c> seam — the guard for the planned
/// subtree-scoped element-direct read optimization ("option a"). Today every read after any write
/// routes through the lifted <c>JsonNode</c> (<c>HasWrites</c> is document-global); option (a) will
/// let reads of paths provably OUTSIDE every write be served zero-copy from the immutable
/// <see cref="JsonElement"/> base again. The risk is a wrong predicate serving STALE base data for a
/// path the write actually affected.
///
/// <para>
/// These cases pin that, after writing path A, reading EVERY path relationship — the written path,
/// a disjoint sibling, an ancestor, a descendant, the root, after a root replace, and after a
/// tombstone (<c>Clear</c>) — returns the same value Newtonsoft produces by mutating-then-reading.
/// They pass today (all reads node-path) and must keep passing once siblings go element-direct.
/// </para>
/// </summary>
public class OverlayWriteThenReadParityTests
{
    private const string Doc = """{"a":{"x":1,"y":2},"b":{"z":3},"list":[10,20]}""";

    /// <summary>Canonical JSON of an STJ context subtree at <paramref name="path"/>.</summary>
    private static string Stj(DataContextImpl ctx, string path)
    {
        using var ms = new MemoryStream();
        ctx.WriteJsonTo(path, ms);
        return ParityJson.Canonicalize(Encoding.UTF8.GetString(ms.ToArray()));
    }

    /// <summary>Canonical JSON of the Newtonsoft oracle's value at <paramref name="path"/>.</summary>
    private static string NsoAt(JToken root, string path) => ParityJson.Nso(root.SelectToken(path)!);

    [Fact]
    public void WriteLeaf_ReadWrittenPath_ReflectsWrite()
    {
        var jt = JObject.Parse(Doc);
        jt["a"]!["x"] = 99;

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a.x", 99);

        Assert.Equal(NsoAt(jt, "$.a.x"), Stj(ctx, "$.a.x")); // 99
    }

    [Fact]
    public void WriteLeaf_ReadDisjointSiblings_Unchanged()
    {
        // THE element-direct-eligible cases: $.b, $.a.y, $.list are all outside the written $.a.x.
        var jt = JObject.Parse(Doc);
        jt["a"]!["x"] = 99;

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a.x", 99);

        Assert.Equal(NsoAt(jt, "$.b"), Stj(ctx, "$.b"));        // {"z":3}
        Assert.Equal(NsoAt(jt, "$.a.y"), Stj(ctx, "$.a.y"));    // 2  (sibling leaf under the modified parent)
        Assert.Equal(NsoAt(jt, "$.list"), Stj(ctx, "$.list"));  // [10,20]
    }

    [Fact]
    public void WriteLeaf_ReadAncestor_ReflectsWrite()
    {
        // Ancestor of the write — must NOT be served stale; the subtree changed.
        var jt = JObject.Parse(Doc);
        jt["a"]!["x"] = 99;

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a.x", 99);

        Assert.Equal(NsoAt(jt, "$.a"), Stj(ctx, "$.a")); // {"x":99,"y":2}
    }

    [Fact]
    public void WriteLeaf_ReadRoot_ReflectsWrite()
    {
        var jt = JObject.Parse(Doc);
        jt["a"]!["x"] = 99;

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a.x", 99);

        Assert.Equal(ParityJson.Nso(jt), Stj(ctx, "$"));
    }

    [Fact]
    public void WriteNewNestedObject_ReadDescendant_ReflectsWrite_SiblingUnchanged()
    {
        var jt = JObject.Parse(Doc);
        ((JObject)jt["a"]!)["created"] = new JObject { ["deep"] = 7 };

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a.created.deep", 7);

        Assert.Equal(NsoAt(jt, "$.a.created"), Stj(ctx, "$.a.created")); // {"deep":7}
        Assert.Equal(NsoAt(jt, "$.b"), Stj(ctx, "$.b"));                 // disjoint sibling unchanged
    }

    [Fact]
    public void WriteArrayElement_ArrayReflectsWrite_DisjointSiblingUnchanged()
    {
        var jt = JObject.Parse(Doc);
        ((JArray)jt["list"]!)[0] = 88;

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.list[0]", 88);

        Assert.Equal(NsoAt(jt, "$.list"), Stj(ctx, "$.list")); // [88,20]
        Assert.Equal(NsoAt(jt, "$.a"), Stj(ctx, "$.a"));       // disjoint sibling unchanged
    }

    [Fact]
    public void RootReplace_ForcesEverythingOffTheBase()
    {
        // After replacing $, the base is no longer authoritative: $.a must be GONE, not served stale.
        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$", new { replaced = true });

        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.a")); // base subtree must not leak through
        Assert.True(ctx.Get<bool>("$.replaced"));
        Assert.Equal(ParityJson.Canonicalize("""{"replaced":true}"""), Stj(ctx, "$"));
    }

    [Fact]
    public void Clear_TombstonesPath_SiblingsUnaffected()
    {
        var jt = JObject.Parse(Doc);
        jt.Remove("a");

        using var doc = JsonDocument.Parse(Doc);
        using var ctx = new DataContextImpl(doc.RootElement);
        ctx.Clear("$.a");

        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.a"));   // tombstoned
        Assert.Equal(NsoAt(jt, "$.b"), Stj(ctx, "$.b"));        // sibling element-direct-eligible, unchanged
        Assert.Equal(NsoAt(jt, "$.list"), Stj(ctx, "$.list"));
    }
}
