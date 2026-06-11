using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.JsonPath;

public class CanonicalPathTests
{
    [Fact]
    public void IsAncestor_RootIsAncestorOfAll()
    {
        Assert.True(CanonicalPath.IsAncestor("$", "$.a.b"));
        Assert.True(CanonicalPath.IsAncestor("$", "$"));
    }

    [Fact]
    public void IsAncestor_PropertyPathSegmentBoundary()
    {
        Assert.True(CanonicalPath.IsAncestor("$.a", "$.a.b"));
        Assert.True(CanonicalPath.IsAncestor("$.a", "$.a[0]"));
        Assert.False(CanonicalPath.IsAncestor("$.a", "$.ab"));
    }

    [Fact]
    public void GetSegments_SplitsCleanly()
    {
        var segments = CanonicalPath.GetSegments("$.a[0].b");
        Assert.Equal(new[] { ".a", "[0]", ".b" }, segments);
    }

    [Fact]
    public void GetParent_ReturnsImmediateParent()
    {
        Assert.Equal("$.a", CanonicalPath.GetParent("$.a.b"));
        Assert.Equal("$.a", CanonicalPath.GetParent("$.a[0]"));
        Assert.Equal("$", CanonicalPath.GetParent("$.a"));
        Assert.Null(CanonicalPath.GetParent("$"));
    }
}
