using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

public class DataContextSelectTests
{
    private static DataContextImpl Ctx(string json) =>
        new DataContextImpl(JsonDocument.Parse(json));

    // ── Select ───────────────────────────────────────────────────────────────

    [Fact]
    public void Select_ReturnsSubContextRootedAtPath()
    {
        using var ctx = Ctx("{\"a\": {\"b\": 7}}");
        using var sub = ctx.Select("$.a");
        Assert.NotNull(sub);
        Assert.Equal(7, sub!.Get<int>("$.b"));
    }

    [Fact]
    public void Select_MissingPath_ReturnsNull()
    {
        using var ctx = Ctx("{\"a\": 1}");
        var sub = ctx.Select("$.missing");
        Assert.Null(sub);
        sub?.Dispose();
    }

    [Fact]
    public void Select_WorksOnNestedArray()
    {
        using var ctx = Ctx("{\"items\": [10, 20, 30]}");
        using var sub = ctx.Select("$.items");
        Assert.NotNull(sub);
        Assert.Equal(3, sub!.Length("$"));
    }

    [Fact]
    public void Select_SubContextIsDetached_WriteDoesNotAffectSource()
    {
        using var ctx = Ctx("{\"a\": {\"v\": 1}}");
        using var sub = ctx.Select("$.a");
        Assert.NotNull(sub);
        sub!.Set("$.v", 99);
        // Source should be unchanged
        Assert.Equal(1, ctx.Get<int>("$.a.v"));
    }

    [Fact]
    public void Select_SubContext_RemainsReadableAfterParentDisposed()
    {
        // Locks the lifetime invariant: the returned context owns its own JsonDocument,
        // so it survives disposal of the parent it was selected from.
        var ctx = Ctx("{\"a\": {\"b\": 7}}");
        var sub = ctx.Select("$.a");
        Assert.NotNull(sub);
        ctx.Dispose();
        Assert.Equal(7, sub!.Get<int>("$.b"));
        sub.Dispose();
    }

    // ── SelectMatches ────────────────────────────────────────────────────────

    [Fact]
    public void SelectMatches_YieldsOneContextPerMatch()
    {
        using var ctx = Ctx("{\"items\": [{\"v\": 1}, {\"v\": 2}]}");
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        Assert.Equal(2, matches.Count);
        Assert.Equal(1, matches[0].Get<int>("$.v"));
        Assert.Equal(2, matches[1].Get<int>("$.v"));
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void SelectMatches_Results_RemainReadableAfterParentDisposed()
    {
        // Each match context owns its own document and survives parent disposal.
        var ctx = Ctx("{\"items\": [{\"v\": 1}, {\"v\": 2}]}");
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        ctx.Dispose();
        Assert.Equal(1, matches[0].Get<int>("$.v"));
        Assert.Equal(2, matches[1].Get<int>("$.v"));
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void SelectMatches_NoMatch_ReturnsEmpty()
    {
        using var ctx = Ctx("{\"items\": []}");
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        Assert.Empty(matches);
    }

    [Fact]
    public void SelectMatches_DetachedWrite_DoesNotChangeSource()
    {
        using var ctx = Ctx("{\"items\": [{\"v\": 1}, {\"v\": 2}]}");
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        // Write to each sub-context
        foreach (var m in matches)
        {
            m.Set("$.v", 99);
        }
        // Source should be unchanged
        var sourceItems = ctx.SelectMatches("$.items[*]").ToList();
        Assert.Equal(1, sourceItems[0].Get<int>("$.v"));
        Assert.Equal(2, sourceItems[1].Get<int>("$.v"));
        foreach (var m in matches) m.Dispose();
        foreach (var m in sourceItems) m.Dispose();
    }

    [Fact]
    public void SelectMatches_WorksWithRecursiveDescent()
    {
        using var ctx = Ctx("{\"a\": {\"x\": 1}, \"b\": {\"x\": 2}}");
        var matches = ctx.SelectMatches("$..x").ToList();
        Assert.Equal(2, matches.Count);
        var values = matches.Select(m => m.Get<int>("$")).OrderBy(v => v).ToList();
        Assert.Equal(new[] { 1, 2 }, values);
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void Select_WorksAfterOverlayWrites()
    {
        using var ctx = Ctx("{\"a\": {\"b\": 1}}");
        ctx.Set("$.a.b", 42);
        using var sub = ctx.Select("$.a");
        Assert.NotNull(sub);
        Assert.Equal(42, sub!.Get<int>("$.b"));
    }

    [Fact]
    public void SelectMatches_WorksAfterOverlayWrites()
    {
        using var ctx = Ctx("{\"items\": [{\"v\": 1}]}");
        ctx.Set("$.items[0].v", 55);
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        var only = Assert.Single(matches);
        Assert.Equal(55, only.Get<int>("$.v"));
        foreach (var m in matches) m.Dispose();
    }
}
