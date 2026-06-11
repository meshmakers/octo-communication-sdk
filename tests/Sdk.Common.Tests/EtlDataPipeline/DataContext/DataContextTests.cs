using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

public class DataContextTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Get_ReadsFromBase()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": 42}"));
        Assert.Equal(42, ctx.Get<int>("$.a"));
    }

    [Fact]
    public void Set_OverlayWins()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": 1}"));
        ctx.Set("$.a", 99);
        Assert.Equal(99, ctx.Get<int>("$.a"));
    }

    [Fact]
    public void Exists_ReturnsTrueForBaseAndOverlay()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": 1}"));
        Assert.True(ctx.Exists("$.a"));
        Assert.False(ctx.Exists("$.missing"));
        ctx.Set("$.added", "x");
        Assert.True(ctx.Exists("$.added"));
    }

    [Fact]
    public void GetKind_ReturnsCorrectKindFromBase()
    {
        var ctx = new DataContextImpl(Doc("{\"o\": {}, \"a\": [], \"s\": \"hi\", \"n\": 1, \"b\": true, \"x\": null}"));
        Assert.Equal(DataKind.Object, ctx.GetKind("$.o"));
        Assert.Equal(DataKind.Array, ctx.GetKind("$.a"));
        Assert.Equal(DataKind.String, ctx.GetKind("$.s"));
        Assert.Equal(DataKind.Number, ctx.GetKind("$.n"));
        Assert.Equal(DataKind.Boolean, ctx.GetKind("$.b"));
        Assert.Equal(DataKind.Null, ctx.GetKind("$.x"));
        Assert.Equal(DataKind.Undefined, ctx.GetKind("$.missing"));
    }

    [Fact]
    public void Length_OnArrayAndString_AndObject()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": [1,2,3], \"s\": \"hello\", \"o\": {\"a\":1,\"b\":2}}"));
        Assert.Equal(3, ctx.Length("$.a"));
        Assert.Equal(5, ctx.Length("$.s"));
        Assert.Equal(2, ctx.Length("$.o"));
    }

    [Fact]
    public void Keys_ReturnsObjectKeys()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": 1, \"b\": 2}"));
        Assert.Equal(new[] { "a", "b" }, ctx.Keys("$").OrderBy(x => x));
    }

    [Fact]
    public async Task IterateArrayAsync_ProvidesChildContextPerItem()
    {
        var ctx = new DataContextImpl(Doc("{\"items\": [10, 20, 30]}"));
        var collected = new List<int>();
        await ctx.IterateArrayAsync("$.items", child =>
        {
            collected.Add(child.Get<int>("$"));
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { 10, 20, 30 }, collected);
    }

    [Fact]
    public async Task IterateMatchesAsync_UsesEvaluator()
    {
        var ctx = new DataContextImpl(Doc(@"{""items"":[{""Id"":""a"",""V"":1},{""Id"":""b"",""V"":2}]}"));
        var collected = new List<int>();
        await ctx.IterateMatchesAsync("$.items[?(@.Id == 'b')]", child =>
        {
            collected.Add(child.Get<int>("$.V"));
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { 2 }, collected);
    }

    [Fact]
    public void GetArray_ReturnsTypedSequence()
    {
        var ctx = new DataContextImpl(Doc("{\"nums\": [1, 2, 3]}"));
        Assert.Equal(new[] { 1, 2, 3 }, ctx.GetArray<int>("$.nums"));
    }

    [Fact]
    public void SelectMatches_SingleMatch_ReturnsOneContext()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": {\"value\": 42}}"));
        var matches = ctx.SelectMatches("$.a.value").ToList();
        Assert.Single(matches);
        Assert.Equal(42, matches[0].Get<int>("$"));
        foreach (var m in matches) m.Dispose();
    }

    [Fact]
    public void SelectMatches_Wildcard_ReturnsAllArrayItems()
    {
        var ctx = new DataContextImpl(Doc("{\"items\": [{\"v\": 1}, {\"v\": 2}, {\"v\": 3}]}"));
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        Assert.Equal(3, matches.Count);
        var values = matches.Select(m => m.Get<int>("$.v")).ToArray();
        foreach (var m in matches) m.Dispose();
        Assert.Equal(new[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void SelectMatches_RecursiveDescent_ReturnsAllOccurrences()
    {
        var ctx = new DataContextImpl(Doc(
            "{\"outer\": {\"value\": 10, \"inner\": {\"value\": 20, \"deeper\": {\"value\": 30}}}}"));
        var matches = ctx.SelectMatches("$..value").ToList();
        Assert.Equal(3, matches.Count);
        var values = matches.Select(m => m.Get<int>("$")).OrderBy(v => v).ToArray();
        foreach (var m in matches) m.Dispose();
        Assert.Equal(new[] { 10, 20, 30 }, values);
    }

    [Fact]
    public void SelectMatches_NoMatch_ReturnsEmpty()
    {
        var ctx = new DataContextImpl(Doc("{\"a\": 1}"));
        var matches = ctx.SelectMatches("$.missing").ToList();
        Assert.Empty(matches);
    }

    [Fact]
    public void SelectMatches_AfterOverlayWrite_SeesUpdatedState()
    {
        var ctx = new DataContextImpl(Doc("{\"items\": [{\"v\": 1}]}"));
        ctx.Set("$.items", new[] { new { v = 7 }, new { v = 8 } });
        var matches = ctx.SelectMatches("$.items[*]").ToList();
        var values = matches.Select(m => m.Get<int>("$.v")).ToArray();
        foreach (var m in matches) m.Dispose();
        Assert.Equal(new[] { 7, 8 }, values);
    }

    [Fact]
    public void GetKind_ExplicitOverlayNull_ReturnsNull()
    {
        var ctx = new DataContextImpl(Doc("{}"));
        ctx.Set<object?>("$.x", null);
        Assert.True(ctx.Exists("$.x"));
        Assert.Equal(DataKind.Null, ctx.GetKind("$.x"));
    }

    [Fact]
    public void Set_MergeOnMissingPath_FirstWriteAssigns()
    {
        // Newtonsoft's JObject.Merge into a missing path simply assigns the value
        // (first-write semantics for accumulators / tenant slots). Verify the new
        // overlay's Merge mode honors this rather than throwing.
        var ctx = new DataContextImpl(Doc("{}"));
        ctx.Set("$.x", new JsonObject { ["a"] = 1 },
            DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Merge);

        var node = ctx.Get<JsonNode>("$.x");
        Assert.NotNull(node);
        Assert.Equal(1, node!.AsObject()["a"]!.GetValue<int>());
    }

    [Fact]
    public async Task UpdateMatchesAsync_SingleMatch_AppliesMutation()
    {
        var ctx = new DataContextImpl(Doc("{\"x\": 1}"));
        await ctx.UpdateMatchesAsync("$.x", subCtx =>
        {
            subCtx.Set("$", 99);
            return Task.CompletedTask;
        });
        Assert.Equal(99, ctx.Get<int>("$.x"));
    }

    [Fact]
    public async Task UpdateMatchesAsync_MultipleMatches_AppliesAllMutations()
    {
        var ctx = new DataContextImpl(Doc("{\"items\":[{\"value\":1},{\"value\":2},{\"value\":3}]}"));
        await ctx.UpdateMatchesAsync("$.items[*].value", subCtx =>
        {
            var current = subCtx.Get<int>("$");
            subCtx.Set("$", current * 2);
            return Task.CompletedTask;
        });
        Assert.Equal(2, ctx.Get<int>("$.items[0].value"));
        Assert.Equal(4, ctx.Get<int>("$.items[1].value"));
        Assert.Equal(6, ctx.Get<int>("$.items[2].value"));
    }

    [Fact]
    public async Task UpdateMatchesAsync_NoMatches_NoOp()
    {
        var ctx = new DataContextImpl(Doc("{\"x\":1}"));
        var bodyCalled = false;
        await ctx.UpdateMatchesAsync("$.missing", subCtx =>
        {
            bodyCalled = true;
            return Task.CompletedTask;
        });
        Assert.False(bodyCalled);
        Assert.Equal(1, ctx.Get<int>("$.x"));
    }

    [Fact]
    public async Task UpdateMatchesAsync_AllocationsProportionalToWrites()
    {
        // Build a doc with 100 items each carrying a 10KB payload (~1MB total).
        var arr = new JsonArray();
        for (var i = 0; i < 100; i++)
        {
            arr.Add(new JsonObject
            {
                ["counter"] = 0,
                ["payload"] = new string('x', 10_000)
            });
        }
        var ctx = new DataContextImpl(JsonDocument.Parse(new JsonObject { ["items"] = arr }.ToJsonString()).RootElement);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        // Use per-thread counter so concurrent xUnit tests don't pollute the baseline
        // (GC.GetTotalAllocatedBytes measures the whole process).
        var before = GC.GetAllocatedBytesForCurrentThread();

        await ctx.UpdateMatchesAsync("$.items[*].counter", subCtx =>
        {
            subCtx.Set("$", 1);
            return Task.CompletedTask;
        });

        var delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // Verify mutation
        Assert.Equal(1, ctx.Get<int>("$.items[0].counter"));
        Assert.Equal(1, ctx.Get<int>("$.items[99].counter"));

        // Allocations should be FAR less than the doc size (~1MB).
        // The OLD MultiMatchHelper pattern would deep-clone the whole doc per call, so > 1MB.
        // The NEW UpdateMatchesAsync should be roughly proportional to the writes (100 small overlays + snapshot).
        // Generous threshold to account for snapshot + JsonNode materialization: < 5MB.
        // (If this fails, the impl is round-tripping the whole doc and needs investigation.)
        Assert.True(delta < 5_000_000, $"Expected allocations < 5MB, got {delta / 1024} KB");
    }

    [Fact]
    public void Length_MissingPath_ReturnsZero()
    {
        // L6: Length aligns with Keys' defaults-on-missing model. A path that is
        // absent (Undefined) or explicitly null reports length 0 instead of throwing.
        var ctx = new DataContextImpl(JsonDocument.Parse("{\"a\":1}"));
        Assert.Equal(0, ctx.Length("$.missing"));
        ctx.Set<object?>("$.x", null);
        Assert.Equal(0, ctx.Length("$.x"));
    }

    [Fact]
    public void Keys_MissingPath_ReturnsEmpty()
    {
        // L6: Keys already returns empty for non-Object kinds; assert that it also
        // returns empty (rather than throwing) for a missing path.
        var ctx = new DataContextImpl(JsonDocument.Parse("{\"a\":1}"));
        Assert.Empty(ctx.Keys("$.missing"));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        // L4: DataContextImpl owns a JsonDocument when constructed via the document
        // ctor (or the parameterless ctor). Dispose must release it and tolerate
        // multiple calls.
        var ctx = new DataContextImpl(JsonDocument.Parse("{\"a\":1}"));
        ctx.Dispose();
        ctx.Dispose(); // idempotent — must not throw
    }

    [Fact]
    public void Dispose_OnExternallyOwnedElement_DoesNotDisposeDocument()
    {
        // When constructed from a JsonElement, the caller retains document ownership
        // and Dispose on the context must NOT touch the original document.
        using var ownedByCaller = JsonDocument.Parse("{\"a\":1}");
        var ctx = new DataContextImpl(ownedByCaller.RootElement);
        ctx.Dispose();
        // Caller's document is still usable.
        Assert.Equal(1, ownedByCaller.RootElement.GetProperty("a").GetInt32());
    }
}
