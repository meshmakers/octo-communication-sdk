using System;
using System.Linq;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Phase-0 characterization: ports the root read tests onto a CHILD context. Reads on
/// the child route through the alias path (<c>$.key.*</c>) and the parent fallback —
/// a different code path from the root <see cref="DataContextImpl"/> reads. The
/// unification must keep child reads at parity with root reads.
/// </summary>
public class ChildReadParityTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    private static IDataContext Child(string aliasJson) =>
        new DataContextImpl(Doc("{}")).CreateIterationChild(new[] { ("$.key", Doc(aliasJson)) });

    [Fact]
    public void Child_GetInt()
    {
        var c = Child("{\"n\":42}");
        Assert.Equal(42, c.Get<int>("$.key.n"));
    }

    [Fact]
    public void Child_GetLong()
    {
        var c = Child("{\"n\":9000000000}");
        Assert.Equal(9000000000L, c.Get<long>("$.key.n"));
    }

    [Fact]
    public void Child_GetDouble()
    {
        var c = Child("{\"n\":1.5}");
        Assert.Equal(1.5d, c.Get<double>("$.key.n"));
    }

    [Fact]
    public void Child_GetDateTime()
    {
        // Mirrors DataContextGetValueTests' ISO-string-to-DateTime case. Get<DateTime>
        // deserializes the JSON string via STJ's ISO-8601 handling.
        var c = Child("{\"d\":\"2024-06-15T10:00:00\"}");
        Assert.Equal(new DateTime(2024, 6, 15, 10, 0, 0), c.Get<DateTime>("$.key.d"));
    }

    [Fact]
    public void Child_TryGet_Missing_ReturnsFalse()
    {
        var c = Child("{\"n\":42}");
        var found = c.TryGet<int>("$.key.missing", out var value);
        Assert.False(found);
        Assert.Equal(default(int), value);
    }

    [Fact]
    public void Child_TryGet_Present_ReturnsTrue()
    {
        var c = Child("{\"n\":42}");
        var found = c.TryGet<int>("$.key.n", out var value);
        Assert.True(found);
        Assert.Equal(42, value);
    }

    [Fact]
    public void Child_GetKind_Missing_Undefined()
    {
        var c = Child("{\"n\":42}");
        Assert.Equal(DataKind.Undefined, c.GetKind("$.key.missing"));
    }

    [Fact]
    public void Child_Length_OnArray()
    {
        var c = Child("{\"arr\":[1,2,3,4]}");
        Assert.Equal(4, c.Length("$.key.arr"));
    }

    [Fact]
    public void Child_Keys_OnObject()
    {
        var c = Child("{\"a\":1,\"b\":2}");
        Assert.Equal(new[] { "a", "b" }, c.Keys("$.key").OrderBy(k => k));
    }

    [Fact]
    public void Child_Select_SurvivesParentDispose()
    {
        // Mirrors DataContextSelectTests.Select_SubContext_RemainsReadableAfterParentDisposed:
        // the Select'd sub-context owns its own JsonDocument and survives disposal of the
        // context it was selected from.
        var root = new DataContextImpl(Doc("{}"));
        var child = root.CreateIterationChild(new[] { ("$.key", Doc("{\"n\":7}")) });
        var sub = child.Select("$.key");
        Assert.NotNull(sub);
        root.Dispose();
        child.Dispose();
        Assert.Equal(7, sub!.Get<int>("$.n"));
        sub.Dispose();
    }
}
