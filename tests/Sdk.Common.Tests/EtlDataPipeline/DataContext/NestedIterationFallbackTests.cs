using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Phase-0 characterization: pins the L3 nested-iteration parent-fallback chain.
/// A grandchild context (created from a middle child) must read through the MIDDLE
/// child's overlay/aliases before reaching the root — the unification must keep the
/// fallback chain rooted at the immediate parent, not jump straight to the root.
/// </summary>
public class NestedIterationFallbackTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>
    /// Creates a grandchild rooted at <paramref name="middle"/> via the internal
    /// <see cref="IIterationContextFactory"/> surface — the same path
    /// ForEachNode/ObjectIteratorNode use to nest iteration contexts.
    /// </summary>
    private static IDataContext Grandchild(IDataContext middle,
        params (string AliasPath, JsonElement Value)[] aliases) =>
        ((IIterationContextFactory)middle).CreateIterationChild(aliases, item: null);

    [Fact]
    public void Grandchild_ReadsMiddleChildPlainOverlayWrite()
    {
        var root = new DataContextImpl(Doc("{}"));
        var middle = root.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());
        middle.Set("$.mw", 5); // plain (non-aliased) overlay write on the middle child

        var grand = Grandchild(middle);

        // CURRENT BEHAVIOUR: the grandchild's fallback chain reads through the MIDDLE
        // child (DataContextChild passes `this`, not the root, when creating nested
        // children), so a plain overlay write on the middle is visible to the grandchild.
        Assert.Equal(5, grand.Get<int>("$.mw"));
    }

    [Fact]
    public void Grandchild_ReadsTwoLevelAlias()
    {
        var root = new DataContextImpl(Doc("{}"));
        var middle = root.CreateIterationChild(new[] { ("$.a", Doc("{\"x\":1}")) });
        var grand = Grandchild(middle, ("$.b", Doc("{\"y\":2}")));

        // CURRENT BEHAVIOUR: the grandchild resolves its own alias ($.b) directly and
        // falls through to the middle child for the middle's alias ($.a), so both
        // levels of alias are readable from the grandchild.
        Assert.Equal(1, grand.Get<int>("$.a.x"));
        Assert.Equal(2, grand.Get<int>("$.b.y"));
    }
}
