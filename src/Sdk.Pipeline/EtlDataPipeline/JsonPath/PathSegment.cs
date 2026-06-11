namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Base type for a parsed JSONPath segment.
/// </summary>
public abstract record PathSegment;

/// <summary>
/// Root segment <c>$</c> at the start of an expression.
/// </summary>
public sealed record RootSegment : PathSegment;

/// <summary>
/// Property access segment, e.g. <c>.Foo</c>.
/// </summary>
/// <param name="Name">Property name.</param>
public sealed record PropertySegment(string Name) : PathSegment;

/// <summary>
/// Array index segment, e.g. <c>[0]</c>.
/// </summary>
/// <param name="Index">Zero-based array index.</param>
public sealed record IndexSegment(int Index) : PathSegment;

/// <summary>
/// Wildcard segment <c>*</c> matching all children of the current node.
/// </summary>
public sealed record WildcardSegment : PathSegment;

/// <summary>
/// Recursive descent segment <c>..</c> matching at any depth.
/// </summary>
public sealed record RecursiveDescentSegment : PathSegment;

/// <summary>
/// Equality filter on a relative property path: [?(@.Foo == 'literal')].
/// Only string-literal equality is supported per spec §6.2.
/// </summary>
/// <param name="RelativeProperty">Relative property path from <c>@</c>.</param>
/// <param name="Literal">String literal compared against the property value.</param>
public sealed record FilterSegment(IReadOnlyList<string> RelativeProperty, string Literal) : PathSegment;

/// <summary>
/// Parsed JSONPath expression as an ordered list of segments.
/// </summary>
/// <param name="Segments">Ordered segments composing the expression.</param>
public sealed record JsonPathExpression(IReadOnlyList<PathSegment> Segments);
