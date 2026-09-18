using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Read-side parity twin of <c>NewtonsoftParityDoubleConverter</c> for <see cref="int" />.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json's built-in Int32 converter inspects the <b>raw text</b> of a number token:
/// <c>reader.GetInt32()</c> rejects the literal <c>5.0</c> even though the value is integral.
/// Newtonsoft's <c>(int)JToken</c> instead goes through <c>Convert.ToInt32(double, InvariantCulture)</c>
/// and accepts it.
/// </para>
/// <para>
/// That matters because the <b>write</b> side deliberately emits a trailing <c>.0</c> for every
/// integral double (see <c>NewtonsoftParityDoubleConverter</c>: a double <c>0.0</c> written as the
/// JSON literal <c>0</c> would round-trip back as <see cref="long" /> and land in MongoDB as a
/// <c>BsonInt64</c> where Newtonsoft stored a <c>BsonDouble</c>). So every node that writes a double
/// — <c>Math@1</c>, <c>LinearScaler@1</c>, <c>SumAggregation@1</c>, <c>ConvertDataType@1</c> to
/// Double, <c>ExecuteCSharp@1</c> returning a double — stores <c>5.0</c>, and every downstream
/// <c>Get&lt;int&gt;</c> on it threw. The strict built-in is a parity regression from the
/// Newtonsoft to System.Text.Json migration, not a designed contract (AB#5275).
/// </para>
/// <para>
/// Coercion rules, verified against the Newtonsoft oracle by
/// <c>Sdk.Common.PipelineParityTests.IntegerCoercionParityTests</c>:
/// </para>
/// <list type="bullet">
/// <item>exact integral literal — built-in fast path, no coercion, no precision loss</item>
/// <item>real literal or exponent — banker's rounding (<see cref="MidpointRounding.ToEven" />),
/// matching <c>Convert.ToInt32(double)</c>: <c>5.0</c> to 5, <c>5.7</c> to 6, <c>5.5</c> to 6,
/// <c>4.5</c> to 4, <c>-5.5</c> to -6</item>
/// <item>JSON string — honors <see cref="JsonNumberHandling.AllowReadingFromString" />, integer
/// first and then real-tolerant</item>
/// <item>out of range or NaN — <see cref="JsonException" />, deliberately NOT
/// <see cref="OverflowException" /> (which is what <c>Convert.ToInt32</c> would throw), because
/// <c>DateTimeNode</c> catches <see cref="JsonException" /> to build its own
/// <c>InvalidUnixTimestamp</c> error</item>
/// </list>
/// <para>
/// This does NOT touch <c>JsonScalar.ToClr</c>, the <b>dynamic</b> boxing path behind
/// <c>IDataContext.GetValue()</c> and <c>RtAttributesConverter</c>. Its contract is explicitly
/// "reals stay double"; coercing there would bring the BsonInt64 regression back. Only explicitly
/// typed reads change.
/// </para>
/// <para>
/// Two deliberate divergences from the oracle, pinned by the parity suite: a quoted real
/// (<c>"5.0"</c>) coerces here but throws in Newtonsoft, so the quoted and unquoted forms behave
/// alike for REST sources that stringify numbers; and a JSON boolean throws here but yields 1 in
/// Newtonsoft, because silently turning <c>true</c> into a number hides a real data bug.
/// </para>
/// <para>
/// Writing is byte-identical to the built-in converter.
/// </para>
/// </remarks>
public sealed class NewtonsoftParityInt32Converter : JsonConverter<int>
{
    /// <inheritdoc />
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                // Exact first: identical to the built-in, and no value ever detours through double.
                if (reader.TryGetInt32(out var exact)) return exact;
                if (reader.TryGetDouble(out var real)) return FromDouble(real);
                break;

            case JsonTokenType.String
                when (options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0:
                // A custom converter bypasses STJ's built-in NumberHandling, so the flag has to be
                // honored explicitly here or AllowReadingFromString would silently stop working.
                var s = reader.GetString();
                if (s is not null)
                {
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }

                    if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out var parsedReal))
                    {
                        return FromDouble(parsedReal);
                    }
                }

                break;
        }

        throw new JsonException(
            $"The JSON value (token '{reader.TokenType}') could not be converted to System.Int32.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    /// <inheritdoc />
    public override int ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return int.Parse(reader.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
    }

    private static int FromDouble(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.ToEven);
        // Both Int32 bounds are exactly representable as double, so plain comparisons are safe here
        // (unlike the Int64 twin, where MaxValue is not).
        if (double.IsNaN(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        {
            throw new JsonException(
                $"The JSON number {value.ToString("R", CultureInfo.InvariantCulture)} is outside " +
                "the range of System.Int32.");
        }

        return (int)rounded;
    }
}

/// <summary>
/// 64-bit twin of <see cref="NewtonsoftParityInt32Converter" />; see there for the full rationale.
/// </summary>
public sealed class NewtonsoftParityInt64Converter : JsonConverter<long>
{
    // (double)long.MaxValue rounds UP to this value — long.MaxValue itself is not representable as a
    // double. The upper bound must therefore be compared EXCLUSIVELY against it, otherwise the
    // (long) cast below would be out of range for inputs just under 2^63.
    private const double ExclusiveUpperBound = 9223372036854775808.0;

    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                // Exact first: large ids and unix-ms values must never detour through double, which
                // would silently lose precision above 2^53.
                if (reader.TryGetInt64(out var exact)) return exact;
                if (reader.TryGetDouble(out var real)) return FromDouble(real);
                break;

            case JsonTokenType.String
                when (options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0:
                var s = reader.GetString();
                if (s is not null)
                {
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }

                    if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out var parsedReal))
                    {
                        return FromDouble(parsedReal);
                    }
                }

                break;
        }

        throw new JsonException(
            $"The JSON value (token '{reader.TokenType}') could not be converted to System.Int64.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    /// <inheritdoc />
    public override long ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return long.Parse(reader.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
    }

    private static long FromDouble(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.ToEven);
        if (double.IsNaN(rounded) || rounded < long.MinValue || rounded >= ExclusiveUpperBound)
        {
            throw new JsonException(
                $"The JSON number {value.ToString("R", CultureInfo.InvariantCulture)} is outside " +
                "the range of System.Int64.");
        }

        return (long)rounded;
    }
}
