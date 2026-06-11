using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Read-parity tests: for every (input document, JSONPath expression) pair in the
/// corpus, assert that Newtonsoft's <see cref="JToken.SelectTokens(string)"/> and the generic
/// <see cref="JsonPathWalker"/> (over <see cref="ElementView"/>) agree on the matching values
/// (after structural normalization). Paths the new dialect deliberately rejects with
/// <see cref="JsonPathNotSupportedException"/> are skipped — see spec §6.4.
///
/// Because both dialects evaluate top-down/document-order, we expect equivalent
/// orderings for the supported subset (root, dotted, indices, wildcards, recursive
/// descent, string-equality filter). We additionally fall back to a set-equality
/// comparison if the ordered comparison fails, so any benign ordering divergence
/// (e.g. on object-wildcard) does not mask real semantic disagreements.
/// </summary>
public class ReadParityTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (name, json) in ParityCorpus.Inputs())
        {
            // The keyed-machines and hyphenated-keys documents are dedicated
            // regression fixtures for the divergences asserted by
            // NewtonsoftAndStj_AgreeOnPath_KnownDivergence and
            // NewtonsoftAndStj_AgreeOnPath_FormerDivergence_NowAtParity below.
            // Pairing them with the full PathExpressions corpus is unnecessary
            // and would add many incidental cases to the cross product.
            if (name == "keyed-machines.json" || name == "hyphenated-keys.json")
            {
                continue;
            }

            foreach (var path in PathExpressions.All)
            {
                yield return new object[] { name, json, path };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NewtonsoftAndStj_AgreeOnPath(string corpusName, string json, string path)
    {
        // Newtonsoft expected results.
        var jt = JToken.Parse(json);
        var expected = jt.SelectTokens(path)
            .Select(t => NormalizeJson(t.ToString(Newtonsoft.Json.Formatting.None)))
            .ToList();

        // STJ + walker results — skip if path uses an unsupported dialect feature.
        List<string> actual;
        try
        {
            using var doc = JsonDocument.Parse(json);
            actual = JsonPathWalker.Select(new ElementView(doc.RootElement), path)
                .Select(m => NormalizeJson(m.Match.GetRawText()))
                .ToList();
        }
        catch (JsonPathNotSupportedException)
        {
            return; // dialect-out-of-scope by spec §6.4
        }

        Assert.True(
            expected.Count == actual.Count,
            $"Match count differs for corpus='{corpusName}', path='{path}': " +
            $"Newtonsoft={expected.Count}, STJ={actual.Count}");

        // Try ordered structural compare first.
        if (expected.SequenceEqual(actual))
        {
            return;
        }

        // Fall back to set-equality (multiset). Object-wildcard / recursive-descent
        // ordering is not strictly defined; what matters is the matching multiset.
        var expectedSorted = expected.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var actualSorted = actual.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(
            expectedSorted.SequenceEqual(actualSorted),
            $"Match contents differ for corpus='{corpusName}', path='{path}'.\n" +
            $"Newtonsoft: [{string.Join(", ", expectedSorted)}]\n" +
            $"STJ:        [{string.Join(", ", actualSorted)}]");
    }

    /// <summary>
    /// Produces a canonical, whitespace-free, number-normalized JSON representation
    /// suitable for textual comparison between Newtonsoft and STJ output.
    /// Newtonsoft strips trailing zeros (<c>120.0</c>); STJ preserves the source
    /// literal (<c>120.00</c>). We canonicalize by parsing every number through
    /// <see cref="decimal"/> (preferred — exact for the corpus's monetary/sensor
    /// values) and falling back to <see cref="double"/> only for values outside
    /// decimal range. Falls back to the raw string for bare scalars Newtonsoft
    /// emits without quotes.
    /// </summary>
    private static string NormalizeJson(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                WriteCanonical(doc.RootElement, writer);
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return s;
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(NormalizeNumber(element.GetRawText()));
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteRawValue(element.GetRawText());
                break;
        }
    }

    /// <summary>
    /// Normalizes a JSON number literal so that <c>120.0</c>, <c>120.00</c>, and
    /// <c>120.000</c> all produce the same text. Strips trailing zeros after the
    /// decimal point, then strips a trailing decimal point if it ends up bare
    /// (Newtonsoft emits <c>120.0</c>; we'd produce <c>120</c> if we naïvely
    /// removed all trailing zeros, so keep one fractional digit when the original
    /// had a decimal point). Newtonsoft serializes integral floats as <c>120.0</c>,
    /// so we mirror that.
    /// </summary>
    private static string NormalizeNumber(string raw)
    {
        if (raw.Length == 0) return raw;

        // Split off exponent if any (e.g. 1.5e10).
        var ePos = raw.IndexOfAny(new[] { 'e', 'E' });
        var mantissa = ePos >= 0 ? raw.Substring(0, ePos) : raw;
        var exponent = ePos >= 0 ? raw.Substring(ePos) : string.Empty;

        var dotPos = mantissa.IndexOf('.');
        if (dotPos < 0)
        {
            // Pure integer; nothing to normalize.
            return mantissa + exponent;
        }

        var intPart = mantissa.Substring(0, dotPos);
        var fracPart = mantissa.Substring(dotPos + 1);

        // Strip trailing zeros, but keep at least one digit (matches Newtonsoft behavior:
        // 120.00 -> 120.0, not 120).
        var trimmed = fracPart.TrimEnd('0');
        if (trimmed.Length == 0) trimmed = "0";

        return intPart + "." + trimmed + exponent;
    }

    // ------------------------------------------------------------------
    // Known-divergence regression cases (Phase 0 expansion).
    //
    // These cases pair Newtonsoft's evaluation against the new evaluator
    // for paths that PREVIOUSLY produced divergent results — either
    // silently (Section A: object descents under recursive filter) or by
    // throw (Section B: bracket-property and dotted-hyphen). Both are now
    // fixed; every case below passes parity (the theories assert agreement).
    //
    //   - Section A: object children of a recursive filter also have the
    //     predicate applied (review claim #2). Fixed in commit 62f313f —
    //     SelectFilter now iterates object members as well as array
    //     elements, so object-keyed maps no longer lose their matches.
    //   - Section B: bracket-property syntax ($['foo-bar']) and
    //     dotted-hyphen paths previously threw; support was added in
    //     commit a62a281. These cases now pass parity (see
    //     FormerDivergenceCases / Section B below).
    //
    // Section C (review claim #10, double-quoted filter literals) was
    // removed: STJ now accepts them (commit 224878c), and Newtonsoft
    // 13.0.4 also throws on " filter literals, so there is no parity
    // baseline to compare against. STJ behavior is covered by the
    // double-quoted filter-literal case ($[?(@.k=="y")]) in
    // Sdk.Common.Tests/EtlDataPipeline/JsonPath/JsonPathWalkerParityTests.cs.
    //
    // The dedicated documents (keyed-machines.json, hyphenated-keys.json)
    // are excluded from the main Cases() cross product above to keep
    // the existing 102-entry corpus stable; their paths are exercised
    // exclusively by the two regression theories below.
    // ------------------------------------------------------------------

    /// <summary>
    /// Section A — object descents under a recursive filter. Newtonsoft
    /// applies <c>$..[?(...)]</c> to BOTH array elements and object
    /// property values. The evaluator does the same: <c>SelectFilter</c>
    /// iterates object members as well as array elements (fixed in commit
    /// 62f313f, review claim #2), so object-keyed maps match in parity with
    /// Newtonsoft. Every case below passes (the theory asserts agreement).
    /// </summary>
    public static IEnumerable<object[]> KnownDivergenceCases()
    {
        // Object-keyed map: machines is an object, not an array.
        yield return new object[]
        {
            "keyed-machines.json",
            "$..[?(@.Id == 'm2')]",
        };
        yield return new object[]
        {
            "keyed-machines.json",
            "$..[?(@.Id == 'm1')].value",
        };
        yield return new object[]
        {
            "keyed-machines.json",
            "$..[?(@.Status == 'OK')]",
        };

        // Flat-wrap object: filter target is the immediate object value.
        yield return new object[]
        {
            "keyed-machines.json",
            "$..[?(@.Id == 'x')]",
        };

        // Filter into a sub-property of an object-keyed match.
        yield return new object[]
        {
            "keyed-machines.json",
            "$..[?(@.Id == 'm3')].value",
        };
    }

    [Theory]
    [MemberData(nameof(KnownDivergenceCases))]
    public void NewtonsoftAndStj_AgreeOnPath_KnownDivergence(string corpusName, string path)
    {
        var json = ParityCorpus.Inputs().Single(t => t.Name == corpusName).Json;

        // Newtonsoft baseline.
        var jt = JToken.Parse(json);
        var expected = jt.SelectTokens(path)
            .Select(t => NormalizeJson(t.ToString(Newtonsoft.Json.Formatting.None)))
            .ToList();

        // Walker. Unlike the main parity theory, we do NOT swallow
        // JsonPathNotSupportedException here — these cases are regressions,
        // not dialect-out-of-scope skips.
        using var doc = JsonDocument.Parse(json);
        var actual = JsonPathWalker.Select(new ElementView(doc.RootElement), path)
            .Select(m => NormalizeJson(m.Match.GetRawText()))
            .ToList();

        Assert.True(
            expected.Count == actual.Count,
            $"Match count differs for corpus='{corpusName}', path='{path}': " +
            $"Newtonsoft={expected.Count}, STJ={actual.Count}");

        var expectedSorted = expected.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var actualSorted = actual.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(
            expectedSorted.SequenceEqual(actualSorted),
            $"Match contents differ for corpus='{corpusName}', path='{path}'.\n" +
            $"Newtonsoft: [{string.Join(", ", expectedSorted)}]\n" +
            $"STJ:        [{string.Join(", ", actualSorted)}]");
    }

    /// <summary>
    /// Section B — paths that formerly caused the new parser to throw but
    /// are now at parity with Newtonsoft. These cases exercised two parser
    /// improvements that have since landed:
    ///
    ///   - Bracket-property selectors (<c>$['foo-bar']</c>, <c>$['kebab-case']</c>,
    ///     <c>$['Machines']</c>) and dotted-hyphen paths (<c>$.kebab-case.value</c>):
    ///     support landed in commit <c>a62a281</c>. Previously the parser threw
    ///     <c>JsonPathNotSupportedException("bracket-property")</c> on bracket
    ///     selectors and <c>JsonPathException("Unexpected character '-'")</c> on
    ///     dotted-hyphen segments.
    ///
    ///   - Double-quoted filter literals (Section C, commit <c>cfd7cee</c>):
    ///     those entries were removed entirely because Newtonsoft 13.0.4 also
    ///     throws on double-quoted filter literals — there is no Newtonsoft
    ///     behavior to be at parity with. The STJ behavior is covered by the
    ///     double-quoted filter-literal case in
    ///     <c>Sdk.Common.Tests/EtlDataPipeline/JsonPath/JsonPathWalkerParityTests.cs</c>.
    ///
    /// All six cases below now pass parity. The data set is kept here so that
    /// regressions on hyphenated-key and bracket-property paths are caught by
    /// the theory.
    /// </summary>
    public static IEnumerable<object[]> FormerDivergenceCases()
    {
        // Bracket-property syntax with hyphenated keys (commit a62a281).
        // Exercises the bracket-property parser branch with single-quoted keys
        // that contain hyphens — previously threw JsonPathNotSupportedException.
        yield return new object[]
        {
            "hyphenated-keys.json",
            "$['foo-bar']",
        };
        yield return new object[]
        {
            "hyphenated-keys.json",
            "$['kebab-case'].value",
        };
        yield return new object[]
        {
            "hyphenated-keys.json",
            "$['kebab-case']",
        };

        // Dotted-hyphen form (commit a62a281). Distinct parser path from
        // bracket-property: exercises the dotted segment splitter on '-'.
        // Previously IsIdentifierChar treated '-' as a terminator so the
        // parser threw JsonPathException("Unexpected character '-'").
        // Newtonsoft accepts hyphens inside dotted property names directly.
        yield return new object[]
        {
            "hyphenated-keys.json",
            "$.kebab-case.value",
        };
        yield return new object[]
        {
            "hyphenated-keys.json",
            "$.foo-bar",
        };

        // Bracket-quoted property selector with a non-hyphenated key (commit a62a281).
        // Exercises $['Machines'] as an alias for $.Machines — the bracket-property
        // branch now handles plain identifiers as well as hyphenated ones.
        yield return new object[]
        {
            "machine-statuses.json",
            "$['Machines'][0].Id",
        };
    }

    [Theory]
    [MemberData(nameof(FormerDivergenceCases))]
    public void NewtonsoftAndStj_AgreeOnPath_FormerDivergence_NowAtParity(string corpusName, string path)
    {
        var json = ParityCorpus.Inputs().Single(t => t.Name == corpusName).Json;

        // Newtonsoft baseline — must produce at least one match, otherwise
        // the case isn't a meaningful regression target.
        var jt = JToken.Parse(json);
        var expected = jt.SelectTokens(path)
            .Select(t => NormalizeJson(t.ToString(Newtonsoft.Json.Formatting.None)))
            .ToList();
        Assert.NotEmpty(expected);

        // Walker. Do not catch JsonPathNotSupportedException — that
        // is the regression we're documenting.
        using var doc = JsonDocument.Parse(json);
        var actual = JsonPathWalker.Select(new ElementView(doc.RootElement), path)
            .Select(m => NormalizeJson(m.Match.GetRawText()))
            .ToList();

        var expectedSorted = expected.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var actualSorted = actual.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(
            expectedSorted.SequenceEqual(actualSorted),
            $"Match contents differ for corpus='{corpusName}', path='{path}'.\n" +
            $"Newtonsoft: [{string.Join(", ", expectedSorted)}]\n" +
            $"STJ:        [{string.Join(", ", actualSorted)}]");
    }
}
