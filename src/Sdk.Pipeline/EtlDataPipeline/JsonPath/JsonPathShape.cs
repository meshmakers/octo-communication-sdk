namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Classifies a JSONPath expression as single-match (addresses at most one value) or multi-match
/// (may address N values). Read paths that must materialise ALL matches — <c>GetArray&lt;T&gt;</c>
/// since AB#5351 — consult this before taking a single-value fast path, because
/// <c>IReadSource.TryGetElement</c> / <c>TryGetNode</c> resolve a path with
/// <c>JsonPathWalker.Select(...).FirstOrDefault()</c> and therefore collapse N matches into the
/// first one.
/// </summary>
internal static class JsonPathShape
{
    /// <summary>
    /// True when <paramref name="path"/> contains a wildcard (<c>[*]</c>), a recursive descent
    /// (<c>..</c>) or a filter (<c>[?(@.k=='v')]</c>) segment — the three constructs the OctoMesh
    /// evaluator supports that can yield more than one match.
    /// </summary>
    /// <remarks>
    /// Two stages on purpose. The pre-filter is an allocation-free character test: every
    /// multi-match construct carries a <c>*</c>, a <c>?</c> or a <c>..</c>, so an ordinary
    /// property/index path (the overwhelming majority of reads) is answered without parsing
    /// anything — which is what keeps the hot single-value read paths allocation-identical.
    /// A hit is only a CANDIDATE, because those characters may also sit inside a quoted property
    /// name (<c>$['a*b']</c>); the decision is therefore confirmed against
    /// <see cref="JsonPathParser"/> itself rather than by re-implementing its grammar here, so a
    /// future multi-match segment kind is classified correctly without a second edit.
    /// A malformed or unsupported path throws out of the parser exactly as the subsequent walk
    /// would have.
    /// </remarks>
    public static bool IsMultiMatch(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!path.Contains('*') && !path.Contains('?') && !path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var segment in JsonPathParser.Parse(JsonNodePath.NormalizePathOrRelative(path)).Segments)
        {
            if (segment is WildcardSegment or RecursiveDescentSegment or FilterSegment) return true;
        }

        return false;
    }
}
