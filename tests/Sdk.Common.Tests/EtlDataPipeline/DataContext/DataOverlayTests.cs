using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

public class DataOverlayTests
{
    private static JsonElement Base(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Read_NoOverlay_ReturnsBaseValue()
    {
        var overlay = new DataOverlay(Base("{\"a\": 1}"));
        Assert.True(overlay.TryRead("$.a", out var node));
        Assert.Equal(1, node!.GetValue<int>());
    }

    [Fact]
    public void Write_ThenRead_ReturnsWrittenValue()
    {
        var overlay = new DataOverlay(Base("{\"a\": 1}"));
        overlay.Write("$.a", JsonValue.Create(42));
        Assert.True(overlay.TryRead("$.a", out var node));
        Assert.Equal(42, node!.GetValue<int>());
    }

    [Fact]
    public void Write_DeepDescendant_AncestorReadObservesIt()
    {
        // Spec §5.1 invariant.
        var overlay = new DataOverlay(Base("{\"a\": {\"b\": 1, \"c\": 2}}"));
        overlay.Write("$.a.b", JsonValue.Create(99));

        Assert.True(overlay.TryRead("$.a.b", out var b));
        Assert.Equal(99, b!.GetValue<int>());

        Assert.True(overlay.TryRead("$.a", out var a));
        var aObj = a!.AsObject();
        Assert.Equal(99, aObj["b"]!.GetValue<int>());
        Assert.Equal(2, aObj["c"]!.GetValue<int>());
    }

    [Fact]
    public void Write_ToRoot_RootReadReflectsIt()
    {
        var overlay = new DataOverlay(Base("{\"a\": 1}"));
        overlay.Write("$", JsonNode.Parse("{\"x\": 99}"));
        Assert.True(overlay.TryRead("$", out var node));
        Assert.Equal(99, node!.AsObject()["x"]!.GetValue<int>());
    }

    [Fact]
    public void Write_DisjointSubtrees_BothObservableFromRoot()
    {
        var overlay = new DataOverlay(Base("{\"a\": {\"b\": 1}, \"c\": {\"d\": 2}}"));
        overlay.Write("$.a.b", JsonValue.Create(11));
        overlay.Write("$.c.d", JsonValue.Create(22));

        Assert.True(overlay.TryRead("$", out var root));
        var rootObj = root!.AsObject();
        Assert.Equal(11, rootObj["a"]!.AsObject()["b"]!.GetValue<int>());
        Assert.Equal(22, rootObj["c"]!.AsObject()["d"]!.GetValue<int>());
    }

    [Fact]
    public void Read_UnrelatedPath_StillHitsBase()
    {
        var overlay = new DataOverlay(Base("{\"a\": {\"b\": 1}, \"c\": 2}"));
        overlay.Write("$.a.b", JsonValue.Create(99));

        Assert.True(overlay.TryRead("$.c", out var c));
        Assert.Equal(2, c!.GetValue<int>());
    }

    [Fact]
    public void Write_OverlappingThenRead_LatestWins()
    {
        var overlay = new DataOverlay(Base("{\"a\": {\"b\": 1}}"));
        overlay.Write("$.a.b", JsonValue.Create(11));
        overlay.Write("$.a.b", JsonValue.Create(22));

        Assert.True(overlay.TryRead("$.a.b", out var b));
        Assert.Equal(22, b!.GetValue<int>());
    }

    [Fact]
    public void Write_AddsNewProperty_VisibleFromAncestor()
    {
        var overlay = new DataOverlay(Base("{\"a\": {\"b\": 1}}"));
        overlay.Write("$.a.newProp", JsonValue.Create("hello"));

        Assert.True(overlay.TryRead("$.a", out var a));
        var aObj = a!.AsObject();
        Assert.Equal(1, aObj["b"]!.GetValue<int>());
        Assert.Equal("hello", aObj["newProp"]!.GetValue<string>());
    }

    [Fact]
    public void Write_RootToNull_HasWritesIsTrue_TryReadReturnsNull()
    {
        // Regression: previously `HasWrites` returned false after Write("$", null) because
        // the only liftedness signal was `_lifted is not null`. That meant TryRead would
        // fall through to the base, masking the explicit null write — iterating an array
        // [1, null, 3] would yield {} (the base) for element 2 instead of null.
        var overlay = new DataOverlay(Base("{\"a\": 1}"));
        overlay.Write("$", null);

        Assert.True(overlay.HasWrites, "HasWrites must report true after an explicit null root write");
        Assert.True(overlay.TryRead("$", out var node),
            "TryRead must succeed (returning present-but-null) after Write(\"$\", null)");
        Assert.Null(node);
    }

    [Fact]
    public void Write_RootToNull_ThenWriteValue_NewValueWins()
    {
        // Sanity check that the _isLifted flag does not get stuck on null.
        var overlay = new DataOverlay(Base("{\"a\": 1}"));
        overlay.Write("$", null);
        overlay.Write("$", JsonNode.Parse("{\"b\": 2}"));

        Assert.True(overlay.TryRead("$", out var node));
        Assert.NotNull(node);
        Assert.Equal(2, node!.AsObject()["b"]!.GetValue<int>());
    }

    [Fact]
    public void Write_NestedAfterRootNull_PromotesToEmptyObject()
    {
        // After an explicit Write("$", null) followed by a nested Write("$.x", v),
        // the null root is replaced by a fresh empty object holding the new property.
        // The previous base ({"a": 1}) MUST NOT come back — the null root was an
        // authoritative reset, and re-materializing base subtrees would silently undo it.
        var overlay = new DataOverlay(Base("{\"a\": 1}"));
        overlay.Write("$", null);
        overlay.Write("$.x", JsonValue.Create(99));

        Assert.True(overlay.TryRead("$.x", out var x));
        Assert.Equal(99, x!.GetValue<int>());
        Assert.False(overlay.TryRead("$.a", out _),
            "Base property 'a' must not reappear after a null-root reset");
    }

    [Fact]
    public void Write_RootReplace_OldBaseSubtreesNotVisible()
    {
        // Correctness companion to the #13 EnsureLifted optimization: when a root write
        // happens, the base subtree must be replaced wholesale. (The optimization is
        // about NOT parsing the base before throwing it away — the behavior here is the
        // same, but the path inside DataOverlay is more direct.)
        const string largeBase = "{\"unrelated\": {\"deep\": {\"x\": 1, \"y\": 2, \"z\": 3}}}";
        var overlay = new DataOverlay(Base(largeBase));
        overlay.Write("$", JsonNode.Parse("{\"only\": 42}"));

        Assert.False(overlay.TryRead("$.unrelated", out _),
            "After root replacement, old base subtrees must not be visible");
        Assert.True(overlay.TryRead("$.only", out var only));
        Assert.Equal(42, only!.GetValue<int>());
    }
}
