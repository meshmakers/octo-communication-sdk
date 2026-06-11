using System;
using System.Linq;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Direct tests for the per-iteration CHILD context's <c>SelectMatches</c>
/// (<c>DataContextImpl.DataContextChild</c>), reached in production via
/// <c>ForEach → CreateUpdateInfoNode</c>. The parent
/// <c>DataContextImpl.SelectMatches</c> has a <c>!_overlay.HasWrites</c> fast path;
/// the child historically had none and rebuilt + re-serialized the whole
/// alias-augmented document — including the large <c>$.full</c> (whole-array) alias —
/// on every call. Because <c>CreateUpdateInfoNode</c> calls <c>SelectMatches</c> once
/// per attribute <c>valuePath</c> inside <c>ForEach</c>, that re-materialisation
/// dominated LOH allocation (~4× the document per call).
///
/// These tests pin BOTH the behaviour the optimisation must preserve (correct matches,
/// including alias and recursive-descent paths) AND the allocation ceiling that the
/// pre-fix implementation blew through. Allocation gate mirrors the established pattern
/// in <see cref="Benchmarks.ForEachMemoryBenchmark"/>.
/// </summary>
[Collection("AllocationGates")]
public class DataContextChildSelectMatchesTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    private static IDataContext Child(params (string Alias, JsonElement Value)[] aliases) =>
        new DataContextImpl(El("{}")).CreateIterationChild(aliases);

    // ── correctness (must be preserved across the optimisation) ────────────────

    [Fact]
    public void ChildSelectMatches_ReadsKeyPath()
    {
        using var child = Child(
            ("$.key", El("""{"Headers":{"MessageType":"DATEN_CRMSG"}}""")),
            ("$.full", El("[1,2,3]")));
        var matches = child.SelectMatches("$.key.Headers.MessageType").ToList();
        var only = Assert.Single(matches);
        Assert.Equal("DATEN_CRMSG", only.Get<string>("$"));
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void ChildSelectMatches_WildcardOverKeyArray()
    {
        using var child = Child(
            ("$.key", El("""{"items":[{"v":1},{"v":2},{"v":3}]}""")),
            ("$.full", El("[1,2,3]")));
        var matches = child.SelectMatches("$.key.items[*]").ToList();
        Assert.Equal(new[] { 1, 2, 3 }, matches.Select(m => m.Get<int>("$.v")));
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void ChildSelectMatches_AliasPathStillResolves()
    {
        // Guards alias-pruning: a query into "$.full" must still see the alias.
        using var child = Child(
            ("$.key", El("""{"v":1}""")),
            ("$.full", El("[10,20,30]")));
        var matches = child.SelectMatches("$.full[*]").ToList();
        Assert.Equal(new[] { 10, 20, 30 }, matches.Select(m => m.Get<int>("$")));
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void ChildSelectMatches_RecursiveDescentSpansAllAliases()
    {
        // Guards alias-pruning: recursive descent can match anywhere, so ALL aliases
        // must be in scope (pruning to a single top-level name would drop matches).
        using var child = Child(
            ("$.key", El("""{"x":1}""")),
            ("$.full", El("""{"x":2}""")));
        var values = child.SelectMatches("$..x").Select(m => m.Get<int>("$")).OrderBy(v => v).ToList();
        Assert.Equal(new[] { 1, 2 }, values);
    }

    [Fact]
    public void ChildSelectMatches_NoMatch_ReturnsEmpty()
    {
        using var child = Child(("$.key", El("""{"a":1}""")));
        Assert.Empty(child.SelectMatches("$.key.missing").ToList());
    }

    [Fact]
    public void ChildSelectMatches_DetachedWrite_DoesNotChangeSource()
    {
        using var child = Child(("$.key", El("""{"items":[{"v":1},{"v":2}]}""")));
        var matches = child.SelectMatches("$.key.items[*]").ToList();
        foreach (var m in matches) m.Set("$.v", 99);
        // Re-read source: must be unchanged.
        var reread = child.SelectMatches("$.key.items[*]").ToList();
        Assert.Equal(new[] { 1, 2 }, reread.Select(m => m.Get<int>("$.v")));
        foreach (var m in matches) m.Dispose();
        foreach (var m in reread) m.Dispose();
    }

    // ── allocation (RED until the child gets the no-round-trip path) ────────────

    [Fact]
    public void ChildSelectMatches_KeyQuery_DoesNotScaleWithUnrelatedFullAlias()
    {
        // Reproduces the production hot path: a child carrying a large "$.full" alias
        // (the whole messages array) while CreateUpdateInfoNode reads small "$.key.*"
        // attribute paths once per valuePath. A "$.key" query must NOT re-materialise
        // the unrelated 2 MB "$.full" alias on every call.
        var bigFull = El("\"" + new string('x', 2_000_000) + "\"");          // ~2 MB
        var message = El("""{"Headers":{"MessageType":"DATEN_CRMSG"},"Message":{"a":1}}""");
        using var child = Child(("$.key", message), ("$.full", bigFull));

        // Warm up (JIT / first-call) so startup allocations aren't charged.
        for (var i = 0; i < 3; i++)
            foreach (var m in child.SelectMatches("$.key.Headers.MessageType")) m.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);

        const int iterations = 50;
        for (var i = 0; i < iterations; i++)
            foreach (var m in child.SelectMatches("$.key.Headers.MessageType")) m.Dispose();

        var delta = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Pre-fix: each call re-parses + re-serialises the ~2 MB "$.full" alias several
        // times → ~300 MB over 50 calls. Post-fix (alias pruned, no string round-trip):
        // allocation is proportional to the small "$.key" subtree → a few MB at most.
        const long ceiling = 30L * 1024 * 1024;
        Assert.True(delta < ceiling,
            $"SelectMatches('$.key.*') allocated {delta / (1024 * 1024)} MB over {iterations} calls, " +
            $"exceeding the {ceiling / (1024 * 1024)} MB ceiling — the unrelated $.full alias is being " +
            "re-materialised per call (pre-fix round-trip behaviour).");
    }
}
