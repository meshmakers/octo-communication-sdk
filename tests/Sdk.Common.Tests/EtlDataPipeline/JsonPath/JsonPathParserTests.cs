using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.JsonPath;

public class JsonPathParserTests
{
    [Fact]
    public void Parse_Root_ProducesRootSegmentOnly()
    {
        var expr = JsonPathParser.Parse("$");
        Assert.Single(expr.Segments);
        Assert.IsType<RootSegment>(expr.Segments[0]);
    }

    [Fact]
    public void Parse_DottedPath_ProducesPropertySegments()
    {
        var expr = JsonPathParser.Parse("$.foo.bar");
        Assert.Equal(3, expr.Segments.Count);
        Assert.IsType<RootSegment>(expr.Segments[0]);
        Assert.Equal(new PropertySegment("foo"), expr.Segments[1]);
        Assert.Equal(new PropertySegment("bar"), expr.Segments[2]);
    }

    [Fact]
    public void Parse_PathWithUnderscore_HandlesIdentifierChars()
    {
        var expr = JsonPathParser.Parse("$._items.full_doc");
        Assert.Equal(new PropertySegment("_items"), expr.Segments[1]);
        Assert.Equal(new PropertySegment("full_doc"), expr.Segments[2]);
    }

    [Fact]
    public void Parse_EmptyString_Throws()
    {
        Assert.Throws<JsonPathException>(() => JsonPathParser.Parse(""));
    }

    [Fact]
    public void Parse_BarePathWithoutRoot_Throws()
    {
        var ex = Assert.Throws<JsonPathException>(() => JsonPathParser.Parse("foo.bar"));
        Assert.Contains("must start with '$'", ex.Message);
    }

    [Fact]
    public void Parse_ArrayIndex_ProducesIndexSegment()
    {
        var expr = JsonPathParser.Parse("$.arr[0]");
        Assert.Equal(3, expr.Segments.Count);
        Assert.Equal(new IndexSegment(0), expr.Segments[2]);
    }

    [Fact]
    public void Parse_Wildcard_ProducesWildcardSegment()
    {
        var expr = JsonPathParser.Parse("$.arr[*]");
        Assert.IsType<WildcardSegment>(expr.Segments[2]);
    }

    [Fact]
    public void Parse_WildcardWithDescent_HandlesChain()
    {
        var expr = JsonPathParser.Parse("$.items[*].name");
        Assert.Equal(4, expr.Segments.Count);
        Assert.IsType<WildcardSegment>(expr.Segments[2]);
        Assert.Equal(new PropertySegment("name"), expr.Segments[3]);
    }

    [Fact]
    public void Parse_NegativeIndex_Throws()
    {
        Assert.Throws<JsonPathNotSupportedException>(() => JsonPathParser.Parse("$.arr[-1]"));
    }

    [Fact]
    public void Parse_ArraySlice_ThrowsNotSupported()
    {
        var ex = Assert.Throws<JsonPathNotSupportedException>(() => JsonPathParser.Parse("$.arr[1:3]"));
        Assert.Contains("slice", ex.Feature, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RecursiveDescent_ProducesRecursiveSegment()
    {
        var expr = JsonPathParser.Parse("$..foo");
        Assert.Equal(3, expr.Segments.Count);
        Assert.IsType<RootSegment>(expr.Segments[0]);
        Assert.IsType<RecursiveDescentSegment>(expr.Segments[1]);
        Assert.Equal(new PropertySegment("foo"), expr.Segments[2]);
    }

    [Fact]
    public void Parse_RecursiveDescentWithWildcard_Parses()
    {
        var expr = JsonPathParser.Parse("$..[*]");
        Assert.Equal(3, expr.Segments.Count);
        Assert.IsType<RecursiveDescentSegment>(expr.Segments[1]);
        Assert.IsType<WildcardSegment>(expr.Segments[2]);
    }

    [Fact]
    public void Parse_EqualityFilter_BasicForm()
    {
        var expr = JsonPathParser.Parse("$.items[?(@.Id == 'abc')]");
        var filter = Assert.IsType<FilterSegment>(expr.Segments[2]);
        Assert.Equal(new[] { "Id" }, filter.RelativeProperty);
        Assert.Equal("abc", filter.Literal);
    }

    [Fact]
    public void Parse_EqualityFilter_NestedRelativePath()
    {
        var expr = JsonPathParser.Parse("$.items[?(@.attrs.code == 'X1')]");
        var filter = Assert.IsType<FilterSegment>(expr.Segments[2]);
        Assert.Equal(new[] { "attrs", "code" }, filter.RelativeProperty);
    }

    [Fact]
    public void Parse_EqualityFilter_RecursiveDescentInside()
    {
        // From production: $..[?(@.Id=='Machine_xxx')].Value
        var expr = JsonPathParser.Parse("$..[?(@.Id == 'Machine_1')].Value");
        Assert.IsType<RootSegment>(expr.Segments[0]);
        Assert.IsType<RecursiveDescentSegment>(expr.Segments[1]);
        Assert.IsType<FilterSegment>(expr.Segments[2]);
        Assert.Equal(new PropertySegment("Value"), expr.Segments[3]);
    }

    [Fact]
    public void Parse_FilterWithDoubleEqualsAndNoSpaces_Parses()
    {
        var expr = JsonPathParser.Parse("$.items[?(@.Id=='abc')]");
        var filter = Assert.IsType<FilterSegment>(expr.Segments[2]);
        Assert.Equal("abc", filter.Literal);
    }

    [Fact]
    public void Parse_FilterWithUnsupportedOperator_Throws()
    {
        Assert.Throws<JsonPathNotSupportedException>(() => JsonPathParser.Parse("$.items[?(@.x > 5)]"));
    }

    [Fact]
    public void Parse_FilterWithLogicalOperator_Throws()
    {
        Assert.Throws<JsonPathNotSupportedException>(() => JsonPathParser.Parse("$.items[?(@.x == 'a' && @.y == 'b')]"));
    }

    [Fact]
    public void Parse_FilterWithRegex_Throws()
    {
        Assert.Throws<JsonPathNotSupportedException>(() => JsonPathParser.Parse("$.items[?(@.x =~ /abc/)]"));
    }

    [Fact]
    public void Parse_TrailingRecursiveDescent_Throws()
    {
        var ex = Assert.Throws<JsonPathException>(() => JsonPathParser.Parse("$.."));
        Assert.Contains("..", ex.Message);
    }

    [Fact]
    public void Parse_RecursiveDescentFollowedByDotDot_Throws()
    {
        Assert.Throws<JsonPathException>(() => JsonPathParser.Parse("$....foo"));
    }
}
