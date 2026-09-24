using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// AB#5351 — <c>GetArray&lt;T&gt;</c> on a MULTI-match path (wildcard, recursive descent, filter).
/// </summary>
/// <remarks>
/// Both single-value read representations resolve a path with
/// <c>JsonPathWalker.Select(...).FirstOrDefault()</c>, so before the fix a pipeline author's
/// <c>$.pending.Items[*].RtId</c> got the FIRST match while the overlay was still clean and
/// <c>null</c> once any node had written to the document — the same path, two wrong answers
/// depending on what ran earlier in the pipeline. These tests pin one answer for both
/// representations (clean base, lifted overlay, iteration child) AND that the lenient single-value
/// behaviour around it is untouched: array as-is, scalar widened to a singleton, object / explicit
/// null / absent still <c>null</c>.
/// </remarks>
public class DataContextGetArrayMultiMatchTests
{
    private const string Doc =
        """
        {"pending":{"Items":[{"RtId":"a","Kind":"Doc"},{"RtId":"b","Kind":"Mail"},{"RtId":"c","Kind":"Doc"}]}}
        """;

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>Record target: binds per match, not per document.</summary>
    private sealed record ItemRef(string RtId, string Kind);

    // ── the multi-match forms ──────────────────────────────────────────────────

    [Fact]
    public void Wildcard_CollectsEveryMatch_OnCleanBase()
    {
        using var ctx = new DataContextImpl(El(Doc));

        Assert.Equal(new[] { "a", "b", "c" }, ctx.GetArray<string>("$.pending.Items[*].RtId"));
    }

    [Fact]
    public void Wildcard_CollectsEveryMatch_AfterOverlayLifted()
    {
        // The regression case: any earlier Set lifts the overlay, and the lifted single-path
        // navigation cannot resolve a wildcard at all — this read used to be null.
        using var ctx = new DataContextImpl(El(Doc));
        ctx.Set("$.marker", 1);

        Assert.Equal(new[] { "a", "b", "c" }, ctx.GetArray<string>("$.pending.Items[*].RtId"));
    }

    [Fact]
    public void Wildcard_OverWholeItems_YieldsOneEntryPerItem()
    {
        using var ctx = new DataContextImpl(El(Doc));

        var items = ctx.GetArray<ItemRef>("$.pending.Items[*]")!.ToList();

        Assert.Equal(new[] { "a", "b", "c" }, items.Select(i => i!.RtId));
        Assert.Equal(new[] { "Doc", "Mail", "Doc" }, items.Select(i => i!.Kind));
    }

    [Fact]
    public void Filter_CollectsOnlyMatchingEntries()
    {
        using var ctx = new DataContextImpl(El(Doc));

        Assert.Equal(new[] { "a", "c" },
            ctx.GetArray<string>("$.pending.Items[?(@.Kind=='Doc')].RtId"));
    }

    [Fact]
    public void RecursiveDescent_CollectsMatchesAtAnyDepth()
    {
        using var ctx = new DataContextImpl(El(Doc));

        Assert.Equal(new[] { "a", "b", "c" }, ctx.GetArray<string>("$..RtId"));
    }

    [Fact]
    public void NestedWildcards_CollectAcrossBothLevels()
    {
        using var ctx = new DataContextImpl(El(
            """
            {"groups":[{"items":[{"id":1},{"id":2}]},{"items":[{"id":3}]},{"items":[]}]}
            """));

        Assert.Equal(new[] { 1, 2, 3 }, ctx.GetArray<int>("$.groups[*].items[*].id"));
    }

    [Fact]
    public void Wildcard_KeepsJsonNullMatchesAsDefault()
    {
        using var ctx = new DataContextImpl(El("""{"a":[{"b":"x"},{"b":null},{"b":"z"}]}"""));

        Assert.Equal(new string?[] { "x", null, "z" }, ctx.GetArray<string>("$.a[*].b"));
    }

    [Fact]
    public void Wildcard_SkipsMatchesWhereThePropertyIsAbsent()
    {
        // Walker semantics: a missing property yields no match at all (distinct from a JSON null,
        // which yields a match with the default value above).
        using var ctx = new DataContextImpl(El("""{"a":[{"b":"x"},{"c":1},{"b":"z"}]}"""));

        Assert.Equal(new[] { "x", "z" }, ctx.GetArray<string>("$.a[*].b"));
    }

    // ── empty results ─────────────────────────────────────────────────────────

    [Fact]
    public void Wildcard_WithoutAnyMatch_ReturnsNull()
    {
        using var ctx = new DataContextImpl(El("""{"a":[]}"""));

        Assert.Null(ctx.GetArray<string>("$.a[*].b"));
    }

    [Fact]
    public void Wildcard_OverAbsentContainer_ReturnsNull()
    {
        using var ctx = new DataContextImpl(El(Doc));

        // "no match" and "path form not applicable here" read the same to every caller: null.
        Assert.Null(ctx.GetArray<string>("$.nope[*].RtId"));
    }

    [Fact]
    public void Filter_WithoutAnyMatch_ReturnsNull()
    {
        using var ctx = new DataContextImpl(El(Doc));

        Assert.Null(ctx.GetArray<string>("$.pending.Items[?(@.Kind=='Nope')].RtId"));
    }

    // ── the pre-existing single-value behaviour must not move ──────────────────

    [Fact]
    public void Array_IsReadElementWise()
    {
        using var ctx = new DataContextImpl(El("""{"ids":["a","b"]}"""));

        Assert.Equal(new[] { "a", "b" }, ctx.GetArray<string>("$.ids"));
    }

    [Fact]
    public void Scalar_StaysASingleton()
    {
        using var ctx = new DataContextImpl(El("""{"id":"a"}"""));

        Assert.Equal(new[] { "a" }, ctx.GetArray<string>("$.id"));
    }

    [Fact]
    public void Scalar_StaysASingleton_AfterOverlayLifted()
    {
        using var ctx = new DataContextImpl(El("""{"id":"a"}"""));
        ctx.Set("$.marker", 1);

        Assert.Equal(new[] { "a" }, ctx.GetArray<string>("$.id"));
    }

    [Fact]
    public void Object_ExplicitNull_AndAbsentPath_StayNull()
    {
        using var ctx = new DataContextImpl(El("""{"o":{"x":1},"n":null}"""));

        Assert.Null(ctx.GetArray<string>("$.o"));
        Assert.Null(ctx.GetArray<string>("$.n"));
        Assert.Null(ctx.GetArray<string>("$.nope"));
    }

    [Fact]
    public void QuotedPropertyNameContainingAWildcardChar_IsNotAMultiMatchPath()
    {
        // The classifier's cheap character pre-filter flags this path as a candidate; the parse
        // that follows must still recognise a plain property segment, so the scalar keeps being
        // widened rather than being walked as a wildcard.
        using var ctx = new DataContextImpl(El("""{"a*b":"x","c?d":"y","e..f":"z"}"""));

        Assert.Equal(new[] { "x" }, ctx.GetArray<string>("$['a*b']"));
        Assert.Equal(new[] { "y" }, ctx.GetArray<string>("$['c?d']"));
        Assert.Equal(new[] { "z" }, ctx.GetArray<string>("$['e..f']"));
    }

    // ── read from an iteration child (ForEach@1) ───────────────────────────────

    [Fact]
    public void Wildcard_FromForEachIterationChild()
    {
        // Mirrors ForEachNode: the child carries the whole document as the "$.full" alias and the
        // item written to "$.key" — both reads go through the child's layered source, never the
        // root's element fast path.
        using var root = new DataContextImpl(El($$"""{"all":[{{Doc}}]}"""));
        var factory = (IIterationContextFactory)root;
        var aliases = factory.ResolveAliasElements(new[] { ("$.full", "$.all") });
        using var child = factory.CreateIterationChild(aliases, null);
        child.Set("$.key", JsonNode.Parse(Doc));

        Assert.Equal(new[] { "a", "b", "c" },
            child.GetArray<string>("$.key.pending.Items[*].RtId"));
        Assert.Equal(new[] { "a", "b", "c" },
            child.GetArray<string>("$.full[*].pending.Items[*].RtId"));
        Assert.Equal(new[] { "a", "c" },
            child.GetArray<ItemRef>("$.key.pending.Items[?(@.Kind=='Doc')]")!.Select(i => i!.RtId));
        Assert.Null(child.GetArray<string>("$.key.pending.Items[*].Nope"));
    }

    [Fact]
    public void Wildcard_FromIterationChild_DoesNotReachTheParentDocument()
    {
        // ⚠️ Pre-existing multi-match boundary, NOT introduced with AB#5351 and deliberately not
        // changed by it: a child's multi-match walk runs over its own document (overlay writes +
        // aliases, LayeredSource.BuildEvalRoot) and never over the parent-fallback chain, because
        // merging the parent in would re-materialise the whole parent document per call — the exact
        // allocation that alias pruning removed (AB#4662). So inside a ForEach the outer document is
        // reachable through the "$.full" alias (above), while a bare outer path is not — even though
        // the SINGLE-value read of the same path resolves through the parent just fine.
        using var root = new DataContextImpl(El(Doc));
        var factory = (IIterationContextFactory)root;
        using var child = factory.CreateIterationChild(
            factory.ResolveAliasElements(Array.Empty<(string, string)>()), null);

        Assert.NotNull(child.Get<JsonNode>("$.pending.Items"));        // single value: parent fallback
        Assert.Null(child.GetArray<string>("$.pending.Items[*].RtId")); // multi-match: own document only
    }
}
