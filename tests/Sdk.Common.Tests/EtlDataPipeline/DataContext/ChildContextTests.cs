using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

public class ChildContextTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Child_Reads_FallBackToParent()
    {
        var parent = new DataContextImpl(Doc("{\"shared\": 42, \"more\": [1,2]}"));
        var child = parent.CreateIterationChild(new[] { ("$.key", JsonDocument.Parse("99").RootElement) });
        Assert.Equal(42, child.Get<int>("$.shared"));
        Assert.Equal(99, child.Get<int>("$.key"));
    }

    [Fact]
    public void Child_Writes_DoNotEscapeToParent()
    {
        var parent = new DataContextImpl(Doc("{\"x\": 1}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.x", 99);
        Assert.Equal(99, child.Get<int>("$.x"));
        Assert.Equal(1, parent.Get<int>("$.x")); // parent unchanged
    }

    [Fact]
    public void Child_Aliases_AreReadable()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var item = JsonDocument.Parse("{\"id\":\"a\"}").RootElement;
        var full = JsonDocument.Parse("{\"big\":\"data\"}").RootElement;
        var child = parent.CreateIterationChild(new[] { ("$.key", item), ("$.full", full) });
        Assert.Equal("a", child.Get<string>("$.key.id"));
        Assert.Equal("data", child.Get<string>("$.full.big"));
    }

    [Fact]
    public void Child_Length_OnArrayFromAlias()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var arrEl = Doc("[1,2,3,4]");
        var child = parent.CreateIterationChild(new[] { ("$.items", arrEl) });
        Assert.Equal(4, child.Length("$.items"));
    }

    [Fact]
    public void Child_Length_OnArrayFromOverlay()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.arr", new[] { 10, 20 });
        Assert.Equal(2, child.Length("$.arr"));
    }

    [Fact]
    public void Child_Length_OnObjectFromParentFallback()
    {
        var parent = new DataContextImpl(Doc("{\"shared\": {\"a\": 1, \"b\": 2}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        Assert.Equal(2, child.Length("$.shared"));
    }

    [Fact]
    public void Child_Keys_FromAliasObject()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var objEl = Doc("{\"a\": 1, \"b\": 2}");
        var child = parent.CreateIterationChild(new[] { ("$.it", objEl) });
        Assert.Equal(new[] { "a", "b" }, child.Keys("$.it").OrderBy(x => x));
    }

    [Fact]
    public void Child_Keys_FromOverlay()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.obj", new { x = 1, y = 2 });
        Assert.Equal(new[] { "x", "y" }, child.Keys("$.obj").OrderBy(k => k));
    }

    [Fact]
    public void Child_GetArray_FromAlias()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var arrEl = Doc("[1, 2, 3]");
        var child = parent.CreateIterationChild(new[] { ("$.nums", arrEl) });
        Assert.Equal(new[] { 1, 2, 3 }, child.GetArray<int>("$.nums"));
    }

    [Fact]
    public void Child_GetArray_FromOverlay()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.nums", new[] { 7, 8 });
        Assert.Equal(new[] { 7, 8 }, child.GetArray<int>("$.nums"));
    }

    [Fact]
    public void Child_Set_Append_OnExistingArrayFromAlias()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var arrEl = Doc("[1, 2]");
        var child = parent.CreateIterationChild(new[] { ("$.arr", arrEl) });
        child.Set("$.arr", 3, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);
        Assert.Equal(new[] { 1, 2, 3 }, child.GetArray<int>("$.arr"));
        // Parent unchanged.
        Assert.False(parent.Exists("$.arr"));
    }

    [Fact]
    public void Child_Set_Prepend_OnMissingPathCreatesArray()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.list", "first", DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Prepend);
        Assert.Equal(new[] { "first" }, child.GetArray<string>("$.list"));
    }

    [Fact]
    public void Child_Set_Merge_ObjectWithObject()
    {
        var parent = new DataContextImpl(Doc("{\"obj\": {\"a\": 1}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.obj", new { b = 2 }, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Merge);
        Assert.Equal(1, child.Get<int>("$.obj.a"));
        Assert.Equal(2, child.Get<int>("$.obj.b"));
        // Parent unchanged.
        Assert.False(parent.Exists("$.obj.b"));
    }

    [Fact]
    public void Child_Set_Append_OnNonArrayThrows()
    {
        var parent = new DataContextImpl(Doc("{\"x\": 1}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        Assert.Throws<DataPipelineException>(() =>
            child.Set("$.x", 2, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append));
    }

    [Fact]
    public async Task Child_IterateArrayAsync_VisitsEachItem()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var arrEl = Doc("[10, 20, 30]");
        var child = parent.CreateIterationChild(new[] { ("$.items", arrEl) });
        var collected = new List<int>();
        await child.IterateArrayAsync("$.items", c => { collected.Add(c.Get<int>("$")); return Task.CompletedTask; });
        Assert.Equal(new[] { 10, 20, 30 }, collected);
    }

    [Fact]
    public async Task Child_IterateObjectAsync_VisitsEachProperty()
    {
        var parent = new DataContextImpl(Doc("{\"o\": {\"a\": 1, \"b\": 2}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        var collected = new List<(string, int)>();
        await child.IterateObjectAsync("$.o", (k, c) =>
        {
            collected.Add((k, c.Get<int>("$")));
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { ("a", 1), ("b", 2) }, collected.OrderBy(t => t.Item1));
    }

    [Fact]
    public async Task Child_IterateMatchesAsync_FiltersByJsonPath()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var arrEl = Doc("[{\"Id\":\"a\",\"V\":1},{\"Id\":\"b\",\"V\":2}]");
        var child = parent.CreateIterationChild(new[] { ("$.items", arrEl) });
        var collected = new List<int>();
        await child.IterateMatchesAsync("$.items[?(@.Id == 'b')]", c =>
        {
            collected.Add(c.Get<int>("$.V"));
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { 2 }, collected);
    }

    [Fact]
    public void Child_CopyTo_DeepClonesSourceIntoOverlay()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var srcEl = Doc("{\"a\": 1, \"nested\": {\"b\": 2}}");
        var child = parent.CreateIterationChild(new[] { ("$.src", srcEl) });
        child.CopyTo("$.src", "$.dst");
        Assert.Equal(1, child.Get<int>("$.dst.a"));
        Assert.Equal(2, child.Get<int>("$.dst.nested.b"));
        // Mutate dst; src must be unchanged (deep clone).
        child.Set("$.dst.a", 99);
        Assert.Equal(1, child.Get<int>("$.src.a"));
        // Parent must not see the dst write.
        Assert.False(parent.Exists("$.dst"));
    }

    [Fact]
    public void Child_CopyTo_MissingSourceIsNoOp()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.CopyTo("$.missing", "$.dst");
        Assert.False(child.Exists("$.dst"));
    }

    [Fact]
    public void Child_Overwrite_PreservesParentSiblings()
    {
        // §5.1 invariant: a child Overwrite at a nested path must preserve parent
        // fallback siblings. Without the seeding fix, writing "$.x.y" lifts the empty
        // child base to {x: {y: 99}}, shadowing parent's "$.x.z".
        var parent = new DataContextImpl(Doc("{\"x\":{\"y\":10,\"z\":20}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.x.y", 99); // Overwrite (default)
        var x = child.Get<System.Text.Json.Nodes.JsonNode>("$.x");
        Assert.NotNull(x);
        Assert.Equal(99, x!.AsObject()["y"]!.GetValue<int>());
        Assert.Equal(20, x.AsObject()["z"]!.GetValue<int>()); // sibling preserved
        // Parent unchanged.
        Assert.Equal(10, parent.Get<int>("$.x.y"));
        Assert.Equal(20, parent.Get<int>("$.x.z"));
    }

    [Fact]
    public void Child_Overwrite_PreservesParentSiblings_NestedPath()
    {
        // Deeper nested case: writing $.x.y.z must preserve $.x.y.* siblings AND
        // $.x.* siblings — verified by reading the partial paths back as whole
        // objects (which is what shadows when the overlay shape is malformed).
        var parent = new DataContextImpl(Doc(
            "{\"x\":{\"y\":{\"z\":1,\"w\":2},\"sibling\":\"keep\"}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.x.y.z", 99);

        // Reading $.x.y as a whole object — this is what would shadow without the
        // seeding fix because the lifted overlay has $.x.y = {z:99}, missing w.
        var y = child.Get<System.Text.Json.Nodes.JsonNode>("$.x.y");
        Assert.NotNull(y);
        Assert.Equal(99, y!.AsObject()["z"]!.GetValue<int>());
        Assert.Equal(2, y.AsObject()["w"]!.GetValue<int>());

        // Reading $.x as a whole object — verify x's "sibling" property is present.
        var x = child.Get<System.Text.Json.Nodes.JsonNode>("$.x");
        Assert.NotNull(x);
        Assert.Equal("keep", x!.AsObject()["sibling"]!.GetValue<string>());
    }

    [Fact]
    public void ChildSet_NestedAppend_PreservesParentSiblings()
    {
        // Same §5.1 invariant as Overwrite, but for Append: writing to a nested
        // path with Append must seed parent-fallback siblings at every intermediate
        // ancestor. Without seeding, the lifted overlay {x: {y: [...]}} shadows
        // parent's "$.x.z".
        var parent = new DataContextImpl(Doc("{\"x\":{\"y\":[1,2],\"z\":5}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.x.y", 3, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);

        Assert.Equal(new[] { 1, 2, 3 }, child.GetArray<int>("$.x.y"));
        var x = child.Get<System.Text.Json.Nodes.JsonNode>("$.x");
        Assert.NotNull(x);
        Assert.Equal(5, x!.AsObject()["z"]!.GetValue<int>()); // sibling preserved
        // Parent unchanged.
        Assert.Equal(new[] { 1, 2 }, parent.GetArray<int>("$.x.y"));
        Assert.Equal(5, parent.Get<int>("$.x.z"));
    }

    [Fact]
    public void ChildSet_NestedPrepend_PreservesParentSiblings()
    {
        // Same invariant as Append, for Prepend.
        var parent = new DataContextImpl(Doc("{\"x\":{\"y\":[1,2],\"z\":5}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.x.y", 0, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Prepend);

        Assert.Equal(new[] { 0, 1, 2 }, child.GetArray<int>("$.x.y"));
        var x = child.Get<System.Text.Json.Nodes.JsonNode>("$.x");
        Assert.NotNull(x);
        Assert.Equal(5, x!.AsObject()["z"]!.GetValue<int>()); // sibling preserved
        // Parent unchanged.
        Assert.Equal(new[] { 1, 2 }, parent.GetArray<int>("$.x.y"));
        Assert.Equal(5, parent.Get<int>("$.x.z"));
    }

    [Fact]
    public void ChildSet_NestedMerge_PreservesParentSiblings()
    {
        // Same invariant as Append/Prepend, for Merge: merging at $.x.y must seed
        // $.x's sibling "z" at the intermediate ancestor.
        var parent = new DataContextImpl(Doc("{\"x\":{\"y\":{\"a\":1},\"z\":5}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.x.y", new { b = 2 }, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Merge);

        Assert.Equal(1, child.Get<int>("$.x.y.a"));
        Assert.Equal(2, child.Get<int>("$.x.y.b"));
        var x = child.Get<System.Text.Json.Nodes.JsonNode>("$.x");
        Assert.NotNull(x);
        Assert.Equal(5, x!.AsObject()["z"]!.GetValue<int>()); // sibling preserved
        // Parent unchanged.
        Assert.False(parent.Exists("$.x.y.b"));
        Assert.Equal(5, parent.Get<int>("$.x.z"));
    }

    [Fact]
    public void ChildSet_DeepNestedAppend_PreservesAllAncestorSiblings()
    {
        // Even deeper: parent {a:{b:{c:[1], d:2}}}; child appends at $.a.b.c.
        // Both $.a.b.d (intermediate sibling) AND any siblings of $.a (none here,
        // but the seed walk must still cover every ancestor) must be preserved.
        var parent = new DataContextImpl(Doc("{\"a\":{\"b\":{\"c\":[1],\"d\":2}}}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$.a.b.c", 9, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);

        Assert.Equal(new[] { 1, 9 }, child.GetArray<int>("$.a.b.c"));
        // Read $.a.b as a whole object — this is what shadows when seeding misses
        // the intermediate ancestor.
        var b = child.Get<System.Text.Json.Nodes.JsonNode>("$.a.b");
        Assert.NotNull(b);
        Assert.Equal(2, b!.AsObject()["d"]!.GetValue<int>());
        // Parent unchanged.
        Assert.Equal(new[] { 1 }, parent.GetArray<int>("$.a.b.c"));
        Assert.Equal(2, parent.Get<int>("$.a.b.d"));
    }

    [Fact]
    public void Child_GetKind_ExplicitOverlayNull_ReturnsNull()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set<object?>("$.x", null);
        Assert.True(child.Exists("$.x"));
        Assert.Equal(DataKind.Null, child.GetKind("$.x"));
    }

    [Fact]
    public void Child_WriteJsonTo_WritesNodeToStream()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var srcEl = Doc("{\"id\": \"x\", \"v\": 7}");
        var child = parent.CreateIterationChild(new[] { ("$.it", srcEl) });
        using var ms = new MemoryStream();
        child.WriteJsonTo("$.it", ms);
        var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("x", parsed.RootElement.GetProperty("id").GetString());
        Assert.Equal(7, parsed.RootElement.GetProperty("v").GetInt32());
    }

    [Fact]
    public async Task Child_UpdateMatchesAsync_SingleMatch_AppliesMutation()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var srcEl = Doc("{\"x\": 1}");
        var child = parent.CreateIterationChild(new[] { ("$.it", srcEl) });
        // Seed child overlay $ with a copy of the alias so mutations land in the child's overlay.
        child.Set("$", System.Text.Json.Nodes.JsonNode.Parse(srcEl.GetRawText()));

        await child.UpdateMatchesAsync("$.x", subCtx =>
        {
            subCtx.Set("$", 99);
            return Task.CompletedTask;
        });

        Assert.Equal(99, child.Get<int>("$.x"));
    }

    [Fact]
    public async Task NestedIteration_GrandchildSeesMiddleChildOverlayWrites()
    {
        // L3 — Investigation: when an outer IterateArrayAsync body sets a
        // non-aliased path on its child, can a nested IterateArrayAsync's body
        // (a grandchild) read that path? The grandchild's parent reference is
        // the root, not the immediate middle child, so non-aliased writes on
        // the middle child are scoped to that level.
        var root = new DataContextImpl(Doc(
            "{\"outer\":[{\"o\":1},{\"o\":2}],\"inner\":[10,20]}"));
        var collected = new List<int>();
        await root.IterateArrayAsync(
            "$.outer",
            new[] { ("$.inner", "$.inner") },
            async outerCtx =>
            {
                outerCtx.Set("$.tag", 99); // middle-child overlay write, not an alias
                await outerCtx.IterateArrayAsync(
                    "$.inner",
                    System.Array.Empty<(string, string)>(),
                    innerCtx =>
                    {
                        // If parent chain is rooted (current behavior), inner can't
                        // see $.tag because root has no $.tag. If parent chain is
                        // chained, inner sees 99.
                        collected.Add(innerCtx.Get<int>("$.tag"));
                        return Task.CompletedTask;
                    });
            });
        Assert.Equal(4, collected.Count);
        // Capture observed behavior — drives the L3 decision.
        Assert.All(collected, v => Assert.Equal(99, v));
    }

    [Fact]
    public void ChildClear_PathPresentInParent_HidesParentValue()
    {
        // L1 — Bug: DataContextChild.Clear delegates to overlay.Clear, which has no
        // tombstone. After Clear, child reads of the path fall through to the parent
        // and report the parent's value. Expectation: a child's explicit Clear hides
        // the parent's value from that child's view.
        var parent = new DataContextImpl(Doc("{\"a\":1}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Clear("$.a");
        Assert.False(child.Exists("$.a"));
        Assert.Equal(0, child.Get<int>("$.a")); // default for missing
        // Parent unchanged.
        Assert.Equal(1, parent.Get<int>("$.a"));
    }

    [Fact]
    public void ChildSetExplicitNull_PathPresentInParent_GetReturnsNull()
    {
        // L2 — Bug: GetAsNode in DataContextChild treats overlay-null as "not set" and
        // falls through to parent. Expectation: an explicit overlay null shadows the
        // parent's value.
        var parent = new DataContextImpl(Doc("{\"a\":1}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set<object?>("$.a", null);
        Assert.Equal(DataKind.Null, child.GetKind("$.a"));
        Assert.Null(child.Get<int?>("$.a"));
        // Parent unchanged.
        Assert.Equal(1, parent.Get<int>("$.a"));
    }

    [Fact]
    public async Task Child_UpdateMatchesAsync_MultipleMatches_AppliesAllMutations()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var srcEl = Doc("{\"items\":[{\"value\":1},{\"value\":2},{\"value\":3}]}");
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        child.Set("$", System.Text.Json.Nodes.JsonNode.Parse(srcEl.GetRawText()));

        await child.UpdateMatchesAsync("$.items[*].value", subCtx =>
        {
            var current = subCtx.Get<int>("$");
            subCtx.Set("$", current * 2);
            return Task.CompletedTask;
        });

        Assert.Equal(2, child.Get<int>("$.items[0].value"));
        Assert.Equal(4, child.Get<int>("$.items[1].value"));
        Assert.Equal(6, child.Get<int>("$.items[2].value"));
    }

    /// <summary>
    /// Legacy pipeline YAML drills into <c>$.X.CkTypeId.SemanticVersionedFullName</c> /
    /// <c>$.X.CkTypeId.FullName</c> expecting the historical reflection-emitted object shape
    /// for <c>RtCkId&lt;T&gt;</c>. Post-STJ-fix the value is a JSON string. When the value flows
    /// through an alias binding (e.g. ForEach binds the iterated item to <c>$.key</c>), reads
    /// route through <c>DataContextChild.TryReadAlias</c> — a separate code path from both
    /// <c>JsonPathWalker.SelectProperty</c> and <c>DataOverlay.StepInto</c>. This pins the
    /// virtual-property shim on that third path.
    /// </summary>
    [Theory]
    [InlineData("SemanticVersionedFullName")]
    [InlineData("FullName")]
    public void Child_AliasString_AcceptsVirtualSemanticVersionedFullName(string virtualProperty)
    {
        var parent = new DataContextImpl(Doc("{}"));
        var item = Doc("{\"CkTypeId\":\"EnergyCommunity/Customer\",\"RtId\":\"66004fda527ac79a03ecedd7\"}");
        var child = parent.CreateIterationChild(new[] { ("$.key", item) });

        Assert.Equal("EnergyCommunity/Customer", child.Get<string>($"$.key.CkTypeId.{virtualProperty}"));
        // The bare path also resolves to the same string — the shim is purely additive.
        Assert.Equal("EnergyCommunity/Customer", child.Get<string>("$.key.CkTypeId"));
    }

    [Fact]
    public void Child_AliasString_VirtualProperty_DoesNotMaterializeOnNonString()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var item = Doc("{\"CkTypeId\":{\"nested\":\"obj\"}}");
        var child = parent.CreateIterationChild(new[] { ("$.key", item) });

        // CkTypeId is an Object here, not a String — the virtual property must NOT fire and
        // the path must resolve as a real missing property.
        Assert.Null(child.Get<string>("$.key.CkTypeId.SemanticVersionedFullName"));
    }
}
