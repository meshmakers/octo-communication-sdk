using System.Text;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Byte-parity of the SERIALIZED output (<see cref="IDataContext.WriteJsonTo"/>) against Newtonsoft for
/// documents containing non-ASCII (umlauts/ß) and HTML-sensitive characters (&lt; &gt; &amp;). This is the
/// one gap the read-parity corpus cannot catch: <see cref="ReadParityTests"/>'s <c>NormalizeJson</c>
/// re-serializes BOTH sides through STJ, which masks encoder-escaping differences. Here the RAW bytes
/// are compared, because that is what matters wherever the serialized document is hashed / HMAC'd or
/// compared verbatim to the legacy Newtonsoft wire form.
///
/// <para>
/// Newtonsoft (default <c>StringEscapeHandling</c>) and the SDK's configured
/// <c>UnsafeRelaxedJsonEscaping</c> both emit umlauts and <c>&lt; &gt; &amp;</c> literally; STJ's DEFAULT
/// encoder escapes them to <c>\uXXXX</c>. <c>WriteJsonTo</c> must honour the SDK encoder so its output
/// matches Newtonsoft byte-for-byte.
/// </para>
/// </summary>
public class EncodingParityTests
{
    [Theory]
    [InlineData("Größe")]               // umlaut + ß
    [InlineData("Müller")]               // umlaut
    [InlineData("a<b>c&d")]              // HTML-sensitive
    [InlineData("Ärger & Größe <tag>")] // mixed non-ASCII + HTML
    [InlineData("plain ascii only")]     // control: pure ASCII must already match
    public void WriteJsonTo_Output_MatchesNewtonsoftBytes(string value)
    {
        // Newtonsoft is the oracle: build {"v":<value>} and serialize compact.
        var json = new JObject { ["v"] = value }.ToString(Newtonsoft.Json.Formatting.None);

        using var doc = JsonDocument.Parse(json);
        using var ctx = new DataContextImpl(doc.RootElement);
        using var ms = new MemoryStream();
        ctx.WriteJsonTo("$", ms);
        var actual = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Equal(json, actual);
    }
}
