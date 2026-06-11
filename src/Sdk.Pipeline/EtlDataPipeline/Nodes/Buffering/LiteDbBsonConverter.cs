using System.Text.Json;
using System.Text.Json.Nodes;
using LiteDB;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;

/// <summary>
/// Class to convert between System.Text.Json <see cref="JsonNode"/> values and LiteDB
/// <see cref="BsonValue"/> values for storage in the edge buffer.
/// </summary>
public static class LiteDbBsonConverter
{
    /// <summary>
    /// Converts a <see cref="JsonNode"/> to a <see cref="BsonValue"/>. Used for round-trip
    /// storage of arbitrary JSON shapes in LiteDB. <see langword="null"/> nodes become
    /// <see cref="BsonValue.Null"/>.
    /// </summary>
    public static BsonValue ToBson(JsonNode? node)
    {
        if (node is null)
        {
            return BsonValue.Null;
        }

        switch (node)
        {
            case JsonObject obj:
            {
                var doc = new BsonDocument();
                foreach (var kvp in obj)
                {
                    doc[kvp.Key] = ToBson(kvp.Value);
                }
                return doc;
            }
            case JsonArray array:
            {
                var bsonArray = new BsonArray();
                foreach (var item in array)
                {
                    bsonArray.Add(ToBson(item));
                }
                return bsonArray;
            }
            case JsonValue value:
                return JsonValueToBson(value);
            default:
                throw new NotSupportedException($"Unsupported JsonNode type: {node.GetType().FullName}");
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonNode"/> object root into a flat
    /// <see cref="Dictionary{TKey,TValue}"/> of property name to BSON value (one level deep).
    /// Non-object inputs (including <see langword="null"/>) yield an empty dictionary.
    /// </summary>
    public static Dictionary<string, BsonValue> ToDictionary(JsonNode? node)
    {
        if (node is null)
        {
            return new Dictionary<string, BsonValue>();
        }

        if (node is not JsonObject obj)
        {
            throw new ArgumentException(
                "The provided JsonNode is not an object and cannot be converted to a Dictionary.");
        }

        var dict = new Dictionary<string, BsonValue>();
        foreach (var kvp in obj)
        {
            dict[kvp.Key] = ToBson(kvp.Value);
        }
        return dict;
    }

    /// <summary>
    /// Converts a <see cref="BsonValue"/> back into a <see cref="JsonNode"/>. Returns
    /// <see langword="null"/> for null/missing inputs.
    /// </summary>
    public static JsonNode? FromBson(BsonValue? bsonValue)
    {
        if (bsonValue is null || bsonValue.IsNull)
        {
            return null;
        }

        switch (bsonValue.Type)
        {
            case BsonType.Document:
            {
                var obj = new JsonObject();
                foreach (var kvp in bsonValue.AsDocument)
                {
                    obj[kvp.Key] = FromBson(kvp.Value);
                }
                return obj;
            }
            case BsonType.Array:
            {
                var array = new JsonArray();
                foreach (var item in bsonValue.AsArray)
                {
                    array.Add(FromBson(item));
                }
                return array;
            }
            case BsonType.Int32:
                return JsonValue.Create(bsonValue.AsInt32);
            case BsonType.Int64:
                return JsonValue.Create(bsonValue.AsInt64);
            case BsonType.Double:
                return JsonValue.Create(bsonValue.AsDouble);
            case BsonType.Decimal:
                return JsonValue.Create(bsonValue.AsDecimal);
            case BsonType.String:
                return JsonValue.Create(bsonValue.AsString);
            case BsonType.Boolean:
                return JsonValue.Create(bsonValue.AsBoolean);
            case BsonType.DateTime:
                return JsonValue.Create(bsonValue.AsDateTime);
            case BsonType.Guid:
                return JsonValue.Create(bsonValue.AsGuid.ToString());
            case BsonType.ObjectId:
                return JsonValue.Create(bsonValue.AsObjectId.ToString());
            case BsonType.Binary:
                return JsonValue.Create(Convert.ToBase64String(bsonValue.AsBinary));
            case BsonType.Null:
                return null;
            default:
                throw new NotSupportedException($"Unsupported BsonType: {bsonValue.Type}");
        }
    }

    /// <summary>
    /// Converts a flat dictionary of BSON values back into a <see cref="JsonObject"/>.
    /// </summary>
    public static JsonObject FromDictionary(Dictionary<string, BsonValue> dict)
    {
        var obj = new JsonObject();
        foreach (var kvp in dict)
        {
            obj[kvp.Key] = FromBson(kvp.Value);
        }
        return obj;
    }

    /// <summary>
    /// Merges multiple dictionaries into one. Values for duplicate keys are merged using
    /// <see cref="MergeBsonValues"/>: arrays concatenate, documents merge recursively, and
    /// scalar collisions are wrapped into a 2-element array.
    /// </summary>
    public static Dictionary<string, BsonValue> MergeDictionaries(params Dictionary<string, BsonValue>[] dictionaries)
    {
        var result = new Dictionary<string, BsonValue>();

        foreach (var dict in dictionaries)
        {
            foreach (var kvp in dict)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                if (!result.ContainsKey(key))
                {
                    result[key] = value;
                }
                else
                {
                    result[key] = MergeBsonValues(result[key], value);
                }
            }
        }

        return result;
    }

    private static BsonValue JsonValueToBson(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
            {
                // Preserve strings as strings. STJ's TryGetValue<DateTime>/Guid/byte[]
                // on a JsonElement-backed JsonValue silently parses ISO-8601/Guid-shaped/
                // base64-shaped strings into typed values, which changes the round-trip
                // shape (e.g. "2024-01-01" → "2024-01-01T00:00:00"). Free-form JSON must
                // round-trip as-is. If a caller genuinely needs typed BSON DateTime/Guid/
                // Binary, that belongs on an explicit value-kind path, not a string
                // coercion fallback. Same fix shape as commit 00ed665 in DistinctNode.
                return new BsonValue(value.GetValue<string>());
            }
            case JsonValueKind.Number:
            {
                // Preserve the source JSON type: `1` → BSON Int64; `1.0` → BSON Double.
                // STJ's TryGetValue<long> on a JsonElement-backed JsonValue rejects
                // numbers with a decimal point, so the long path catches genuine
                // integers without coercing integer-valued doubles. Do NOT round
                // double-to-long when Math.Floor(d) == d — that silently changes the
                // round-trip JSON shape (`1.0` → `1`).
                if (value.TryGetValue<long>(out var longValue))
                {
                    return new BsonValue(longValue);
                }

                if (value.TryGetValue<double>(out var doubleValue))
                {
                    return new BsonValue(doubleValue);
                }

                if (value.TryGetValue<decimal>(out var decimalValue))
                {
                    return new BsonValue(decimalValue);
                }

                return new BsonValue(value.ToString());
            }
            case JsonValueKind.True:
                return new BsonValue(true);
            case JsonValueKind.False:
                return new BsonValue(false);
            case JsonValueKind.Null:
                return BsonValue.Null;
            default:
                throw new NotSupportedException($"Unsupported JsonValueKind: {value.GetValueKind()}");
        }
    }

    private static BsonValue MergeBsonValues(BsonValue existingValue, BsonValue newValue)
    {
        if (existingValue.IsArray && newValue.IsArray)
        {
            var mergedArray = existingValue.AsArray;
            mergedArray.AddRange(newValue.AsArray);
            return mergedArray;
        }
        if (existingValue.IsDocument && newValue.IsDocument)
        {
            return MergeBsonDocuments(existingValue.AsDocument, newValue.AsDocument);
        }
        if (existingValue.IsArray)
        {
            var array = existingValue.AsArray;
            array.Add(newValue);
            return array;
        }
        if (newValue.IsArray)
        {
            var array = new BsonArray { existingValue };
            array.AddRange(newValue.AsArray);
            return array;
        }

        return new BsonArray { existingValue, newValue };
    }

    private static BsonDocument MergeBsonDocuments(BsonDocument existingDoc, BsonDocument newDoc)
    {
        foreach (var kvp in newDoc)
        {
            if (existingDoc.TryGetValue(kvp.Key, out var existingValue))
            {
                existingDoc[kvp.Key] = MergeBsonValues(existingValue, kvp.Value);
            }
            else
            {
                existingDoc.Add(kvp.Key, kvp.Value);
            }
        }
        return existingDoc;
    }
}
