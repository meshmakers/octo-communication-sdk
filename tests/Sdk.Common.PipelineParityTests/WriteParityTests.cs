using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Write-parity tests: behavioural parity for the three core <c>Set</c> semantics
/// the legacy Newtonsoft pipeline relied on:
///   1. <b>Overwrite</b> at an existing path.
///   2. <b>Auto-create-missing-path</b> on a deep path through new objects.
///   3. <b>Append</b> on an existing array.
///
/// These mirror the Newtonsoft <c>ReplaceNested</c> / array-append behaviour the
/// pipeline framework implicitly required.
/// </summary>
public class WriteParityTests
{
    [Fact]
    public void SimpleOverwrite_Parity()
    {
        // Newtonsoft expected: replace a top-level scalar.
        var json = "{\"a\": 1}";
        var jt = JObject.Parse(json);
        jt["a"] = 99;
        var expected = Canonicalize(jt.ToString(Newtonsoft.Json.Formatting.None));

        // STJ implementation under test.
        using var doc = JsonDocument.Parse(json);
        var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a", 99);

        using var ms = new MemoryStream();
        ctx.WriteJsonTo("$", ms);
        var actual = Canonicalize(System.Text.Encoding.UTF8.GetString(ms.ToArray()));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetMissingPath_AutoCreatesIntermediateObjects()
    {
        // Newtonsoft's ReplaceNested behaviour — must match: setting "$.a.b.c" on an
        // empty object creates the {a: {b: {c: 42}}} chain.
        using var doc = JsonDocument.Parse("{}");
        var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.a.b.c", 42);

        Assert.Equal(42, ctx.Get<int>("$.a.b.c"));
        Assert.Equal(DataKind.Object, ctx.GetKind("$.a"));
        Assert.Equal(DataKind.Object, ctx.GetKind("$.a.b"));
    }

    [Fact]
    public void Append_ToExistingArray_AddsAtEnd()
    {
        using var doc = JsonDocument.Parse("{\"arr\": [1,2]}");
        var ctx = new DataContextImpl(doc.RootElement);

        ctx.Set("$.arr", 3, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);

        var arr = ctx.GetArray<int>("$.arr")!.ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, arr);
    }

    [Fact]
    public void Append_ToExistingArray_NewtonsoftReference_Parity()
    {
        // Newtonsoft reference: appending 3 to [1,2] yields [1,2,3].
        var jt = JObject.Parse("{\"arr\": [1,2]}");
        ((JArray)jt["arr"]!).Add(3);
        var expected = Canonicalize(jt.ToString(Newtonsoft.Json.Formatting.None));

        using var doc = JsonDocument.Parse("{\"arr\": [1,2]}");
        var ctx = new DataContextImpl(doc.RootElement);
        ctx.Set("$.arr", 3, DocumentModes.Extend, ValueKinds.Simple, TargetValueWriteModes.Append);

        using var ms = new MemoryStream();
        ctx.WriteJsonTo("$", ms);
        var actual = Canonicalize(System.Text.Encoding.UTF8.GetString(ms.ToArray()));

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Re-serializes JSON with default STJ formatting so that whitespace and
    /// number-literal differences (e.g. <c>120.0</c> vs <c>120</c>) do not produce
    /// false negatives between Newtonsoft and STJ output. This is the same
    /// canonicalization concept used by <see cref="ReadParityTests"/>.
    /// </summary>
    private static string Canonicalize(string json)
    {
        using var d = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(d.RootElement);
    }
}
