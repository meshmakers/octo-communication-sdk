using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Single source of test inputs for the attribute-dict parity theories. Each entry is the
/// "typed CLR value" that production code would put in <c>RtTypeWithAttributes.Attributes[…]</c>.
/// Both <c>AttributeRoundTripClrTypeParityTests</c> and <c>AttributeSerializeBytesParityTests</c>
/// drive the same corpus through the production Newtonsoft and System.Text.Json serializers
/// (<c>RtNewtonsoftSerializer.DefaultSerializer</c> and <c>RtSystemTextJsonSerializer.Default</c>),
/// using Newtonsoft as the parity oracle.
/// </summary>
/// <remarks>
/// Add cases here, not inline in the theories. The corpus is intentionally exhaustive — it covers
/// every primitive that the EDA / energy / blueprint code paths put into an attribute dict, plus
/// edge cases (Int32 boundary, integral doubles, high-precision decimals, dates with different
/// kinds and formats, control characters, umlauts, HTML-sensitive chars). Each case carries a
/// stable <c>Name</c> for the test display name and a <c>Value</c> that gets boxed into a
/// <see cref="object"/> dictionary slot.
/// </remarks>
public static class AttributeValueParityCorpus
{
    public sealed record Case(string Name, object? Value);

    public static IEnumerable<Case> All()
    {
        // -------- integers --------
        yield return new("int-zero", 0);
        yield return new("int-one", 1);
        yield return new("int-minus-one", -1);
        yield return new("int-max", int.MaxValue);
        yield return new("int-min", int.MinValue);
        yield return new("long-just-over-int-max", (long)int.MaxValue + 1);
        yield return new("long-max", long.MaxValue);
        yield return new("long-min", long.MinValue);

        // -------- reals --------
        yield return new("double-zero", 0.0);
        yield return new("double-one", 1.0);
        yield return new("double-fractional", 0.5);
        yield return new("double-negative-fractional", -0.5);
        yield return new("double-large-fractional", 123456.789);
        yield return new("double-tiny", 1e-10);
        yield return new("double-huge", 1e20);
        yield return new("float-zero", 0.0f);
        yield return new("float-one", 1.0f);
        yield return new("float-fractional", 0.5f);

        // -------- decimals --------
        yield return new("decimal-zero", 0m);
        yield return new("decimal-one", 1m);
        yield return new("decimal-fractional", 0.5m);
        yield return new("decimal-high-precision", 0.651156m);
        yield return new("decimal-trailing-zeros", 1.0000m);

        // -------- booleans --------
        yield return new("bool-true", true);
        yield return new("bool-false", false);

        // -------- strings --------
        yield return new("string-empty", "");
        yield return new("string-ascii", "hello");
        yield return new("string-umlaut", "Größe");
        yield return new("string-html", "<html>&amp;</html>");
        yield return new("string-control-char", "ab");
        yield return new("string-quote", "she said \"hi\"");
        yield return new("string-backslash", "C:\\path");

        // -------- nulls --------
        yield return new("null", null);

        // -------- DateTime --------
        yield return new("datetime-utc", new DateTime(2026, 5, 27, 14, 0, 0, DateTimeKind.Utc));
        yield return new("datetime-utc-with-ms", new DateTime(2026, 5, 27, 14, 0, 0, 123, DateTimeKind.Utc));
        yield return new("datetime-local", new DateTime(2026, 5, 27, 14, 0, 0, DateTimeKind.Local));
        yield return new("datetime-unspecified", new DateTime(2026, 5, 27, 14, 0, 0, DateTimeKind.Unspecified));

        // -------- DateTimeOffset --------
        yield return new("dto-utc", new DateTimeOffset(2026, 5, 27, 14, 0, 0, TimeSpan.Zero));
        yield return new("dto-positive-offset", new DateTimeOffset(2026, 5, 27, 14, 0, 0, TimeSpan.FromHours(2)));

        // -------- TimeSpan --------
        yield return new("timespan-quarter-hour", TimeSpan.FromMinutes(15));
        yield return new("timespan-zero", TimeSpan.Zero);

        // -------- Guid --------
        yield return new("guid", Guid.Parse("11111111-2222-3333-4444-555555555555"));

        // -------- RtRecord (nested record carried as a dict-slot value) --------
        // Exercises the JsonObject-with-CkRecordId rehydration path that CreateUpdateInfoNode
        // and the SDK's RtAttributesConverter both rely on. Both Newtonsoft and STJ must
        // round-trip back to RtRecord (not a raw JsonObject / JObject).
        yield return new("rtRecord-flat", new RtRecord(
            new RtCkId<CkRecordId>("Basic/Contact"),
            new Dictionary<string, object?>
            {
                ["FirstName"] = "Markus",
                ["LastName"] = "Roider"
            }));
        yield return new("rtRecord-nested", new RtRecord(
            new RtCkId<CkRecordId>("Basic/Contact"),
            new Dictionary<string, object?>
            {
                ["FirstName"] = "Markus",
                ["Address"] = new RtRecord(
                    new RtCkId<CkRecordId>("Basic/Address"),
                    new Dictionary<string, object?>
                    {
                        ["Street"] = "Neufahrn 10",
                        ["Zipcode"] = 5202L
                    })
            }));
    }

    /// <summary>
    /// Names of cases where JSON physics make exact parity with Newtonsoft unachievable.
    /// The parity theories Skip these by name; the documented STJ behaviour for each is
    /// asserted positively in <c>IrreducibleDivergenceTests</c>.
    /// </summary>
    /// <remarks>
    /// Root cause: Newtonsoft's <c>JObject</c>/<c>JValue</c> preserves the source CLR type
    /// in an in-memory side-channel (the boxed <c>Value</c>), so an attribute dict written
    /// from <c>decimal 0.5m</c> reads back as <c>decimal 0.5m</c>. STJ's <c>JsonNode</c>
    /// stores only the JSON DOM — once a number lands as <see cref="System.Text.Json.JsonValueKind.Number"/>
    /// the source CLR type is gone, and the deserializer has only the wire literal to dispatch on.
    /// Consumers needing decimal precision, float type, or DateTimeOffset preservation
    /// must use the typed accessor path (<c>GetAttributeValue&lt;T&gt;</c>, typed property), not
    /// the raw attribute-dict round-trip.
    /// </remarks>
    public static readonly HashSet<string> IrreducibleDivergences = new(StringComparer.Ordinal)
    {
        // float vs double: JSON encodes both as a number token. Newtonsoft preserves
        // float via JValue's typed Value slot; STJ cannot.
        "float-zero",
        "float-one",
        "float-fractional",

        // decimal vs double: same root cause. JSON has one number token type; decimal
        // precision is lost on STJ's deserialize. EnergyQuantity.Quantity (decimal) is
        // safe in practice because the EDA pipeline declares the CK attribute as Double
        // and AttributeValueConverter coerces at the boundary.
        "decimal-zero",
        "decimal-one",
        "decimal-fractional",
        "decimal-high-precision",
        "decimal-trailing-zeros",

        // DateTimeOffset vs DateTime: STJ writes "...+00:00" / "...Z" for both, and the
        // deserialize path returns DateTime. Newtonsoft preserves DateTimeOffset via
        // JValue. No JSON-wire way to recover the original CLR type.
        "dto-utc",
        "dto-positive-offset",
    };

    /// <summary>Stable display string for test names — avoids breaking on culture differences.</summary>
    public static string FormatDisplay(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null-toString>"
    };
}
