using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// CLR-type parity: for every corpus case, round-trip the value through the production Newtonsoft
/// path and through the production System.Text.Json path, then assert the deserialized CLR types
/// are identical. This is the contract that MongoDB BSON serialization (which dispatches on the
/// value's CLR type) and downstream <c>GetAttributeValue&lt;T&gt;</c> consumers depend on.
/// </summary>
/// <remarks>
/// <para>
/// Newtonsoft is the oracle. If STJ produces a different CLR type for the same input, that is a bug
/// in our STJ configuration — to be fixed in <c>RtAttributesConverter</c> / <c>JsonScalar</c> /
/// <c>RtSystemTextJsonSerializer</c>, not by editing this assertion.
/// </para>
/// <para>
/// Some divergences are irreducible (JSON physics — e.g. decimal vs double share one wire token).
/// Those are listed by name in <see cref="AttributeValueParityCorpus.IrreducibleDivergences"/> and
/// skipped here with a documented reason.
/// </para>
/// </remarks>
public class AttributeRoundTripClrTypeParityTests
{
    public static IEnumerable<object[]> Cases() =>
        AttributeValueParityCorpus.All().Select(c => new object[] { c.Name, c.Value! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTrip_Clr_Types_Match_Newtonsoft(string caseName, object? input)
    {
        if (AttributeValueParityCorpus.IrreducibleDivergences.Contains(caseName))
        {
            // Documented in AttributeValueParityCorpus.IrreducibleDivergences.
            return;
        }

        var newtonsoftType = NewtonsoftRoundTrip(input);
        var stjType = StjRoundTrip(input);

        Assert.Equal(newtonsoftType, stjType);
    }

    /// <summary>
    /// In-memory round-trip through Newtonsoft — mirrors the pre-migration production path
    /// (<c>dataContext.SetValueByPath(typed, RtNewtonsoftSerializer)</c> +
    /// <c>GetComplexObjectByPath&lt;T&gt;(RtNewtonsoftSerializer)</c>), which used
    /// <c>JToken.FromObject</c> + <c>JToken.ToObject</c> with <b>no text serialization</b> between
    /// them. <c>JTokenWriter</c> preserves the source CLR type in the resulting <c>JValue</c>
    /// (e.g. <c>int 1</c> → <c>JValue(Type=Integer, Value=Int32(1))</c>), so the
    /// <see cref="RtNewtonsoftAttributesConverter"/> reads back the original boxed type via
    /// <c>jValue.Value</c>. Going through text would widen ints to long inside the writer.
    /// </summary>
    private static Type? NewtonsoftRoundTrip(object? input)
    {
        var dict = new Dictionary<string, object?> { ["v"] = input };
        var serializer = RtNewtonsoftSerializer.DefaultSerializer;

        var jToken = JToken.FromObject(dict, serializer);
        var result = jToken.ToObject<Dictionary<string, object?>>(serializer);

        return result?["v"]?.GetType();
    }

    /// <summary>
    /// In-memory round-trip through System.Text.Json — the actual production path used by
    /// <c>DataContextImpl.Set</c> + <c>Get&lt;T&gt;</c> in <c>DataContext.cs</c>
    /// (<c>JsonSerializer.SerializeToNode</c> + <c>JsonNode.Deserialize&lt;T&gt;</c>). Unlike
    /// Newtonsoft's <c>JTokenWriter</c>, STJ's <see cref="JsonNode"/> does not preserve the source
    /// CLR type — once a number lands in <see cref="JsonValueKind.Number"/>, the deserialize path
    /// has only the wire literal to dispatch on, and <see cref="RtAttributesConverter"/> currently
    /// boxes integers as <see cref="long"/> via <c>JsonScalar.ToClr</c>.
    /// </summary>
    private static Type? StjRoundTrip(object? input)
    {
        IReadOnlyDictionary<string, object?> dict = new Dictionary<string, object?> { ["v"] = input };
        var options = RtSystemTextJsonSerializer.Default;

        var node = System.Text.Json.JsonSerializer.SerializeToNode(dict, options);
        var result = node!.Deserialize<IReadOnlyDictionary<string, object?>>(options);

        return result?["v"]?.GetType();
    }
}
