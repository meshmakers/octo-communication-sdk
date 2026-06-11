using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Characterization tests for §5.1: a child write at a nested path whose ancestor lives
/// ONLY in the parent must preserve the parent's sibling keys. The child overlay base is
/// <c>{}</c>, so a naive lift would shadow parent siblings.
/// </summary>
/// <remarks>
/// CURRENT BEHAVIOUR for the TOP-LEVEL cases below: the write target's only ancestor is the
/// root <c>$</c>, which <c>SeedAncestorsFromParent</c> skips — so no explicit ancestor
/// seeding happens here. Sibling preservation instead falls out of the layered read model:
/// after the child overlay lifts to <c>{list:[...]}</c> (Append/Prepend) or <c>{obj:{...}}</c>
/// (Merge), a read of a DIFFERENT top-level sibling (<c>$.other</c>) misses the overlay and
/// falls through to the parent fallback, which still holds the original value. The lifted
/// child shape never contains <c>other</c>, so it cannot shadow it. The §5.1 seeding walk is
/// what protects NESTED-ancestor siblings (covered in ChildContextTests); the top-level
/// invariant holds purely via parent fallback.
/// </remarks>
public class ChildSetFromParentFallbackTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Child_Append_OnArrayLivingOnlyInParent_PreservesParentSiblings()
    {
        var parent = new DataContextImpl(Doc("{\"list\":[1,2],\"other\":99}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());

        child.Set("$.list", 3, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);

        Assert.Equal(3, child.Length("$.list"));
        Assert.Equal(3, child.Get<int>("$.list[2]"));
        Assert.Equal(99, child.Get<int>("$.other")); // sibling preserved via parent fallback

        // Parent unchanged.
        Assert.Equal(2, parent.Length("$.list"));
    }

    [Fact]
    public void Child_Prepend_OnArrayLivingOnlyInParent_PreservesParentSiblings()
    {
        var parent = new DataContextImpl(Doc("{\"list\":[1,2],\"other\":99}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());

        child.Set("$.list", 0, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Prepend);

        Assert.Equal(3, child.Length("$.list"));
        Assert.Equal(0, child.Get<int>("$.list[0]"));
        Assert.Equal(99, child.Get<int>("$.other")); // sibling preserved via parent fallback

        // Parent unchanged.
        Assert.Equal(2, parent.Length("$.list"));
        Assert.Equal(new[] { 1, 2 }, parent.GetArray<int>("$.list"));
    }

    [Fact]
    public void Child_Merge_OnObjectLivingOnlyInParent_PreservesParentSiblings()
    {
        var parent = new DataContextImpl(Doc("{\"obj\":{\"a\":1},\"other\":7}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());

        child.Set("$.obj", new { b = 2 }, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Merge);

        Assert.Equal(1, child.Get<int>("$.obj.a")); // existing key merged from parent
        Assert.Equal(2, child.Get<int>("$.obj.b")); // new key
        Assert.Equal(7, child.Get<int>("$.other")); // sibling preserved via parent fallback

        // Parent unchanged.
        Assert.False(parent.Exists("$.obj.b"));
    }
}
