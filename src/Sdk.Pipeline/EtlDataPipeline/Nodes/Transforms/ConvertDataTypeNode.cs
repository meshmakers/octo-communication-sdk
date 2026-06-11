using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration data type conversion
/// </summary>
[NodeName("ConvertDataType", 1)]
public record ConvertDataTypeNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Data type that the value is cast to during transformation
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required AttributeValueTypesDto ValueType { get; set; }
}

/// <summary>
/// Convert the value to the defined data type
/// </summary>
[NodeConfiguration(typeof(ConvertDataTypeNodeConfiguration))]
public class ConvertDataTypeNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ConvertDataTypeNodeConfiguration>();

        if (dataContext.GetKind("$") == DataKind.Undefined || dataContext.GetKind("$") == DataKind.Null)
        {
            dataContext.Set<object>(c.TargetPath, null,
                c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
            return;
        }

        var kind = dataContext.GetKind(c.Path);
        if (kind == DataKind.Object || kind == DataKind.Array)
        {
            throw DataPipelineException.ValueIsObjectButMustBePrimitive(c.Path);
        }

        // Resolve via typed Get<T>() so STJ deserializes the source JsonNode/JsonElement
        // straight to the requested CLR type. Get<object>() boxes a JsonElement which
        // does not implement IConvertible, so Convert.ToX would throw.
        var value = ConvertPrimitiveValue(dataContext, c);
        dataContext.Set(c.TargetPath, value, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }

    private static object? ConvertPrimitiveValue(IDataContext dataContext, ConvertDataTypeNodeConfiguration c)
    {
        // For String, Boolean and DateTime we need tolerant coercion semantics that STJ's
        // strict typed deserialization will not provide:
        //   - String: coerce numbers/booleans into their text form (e.g. 6 -> "6").
        //   - Boolean: parse JSON strings like "true"/"false" (AllowReadingFromString does
        //     not extend to bool) and accept 0/1 numerics.
        //   - DateTime: parse ISO-8601 (and other) strings via Convert.ToDateTime with
        //     InvariantCulture for deterministic behavior.
        // Read the underlying JsonNode and convert it ourselves so the legacy "tolerant"
        // conversion semantics are preserved.
        if (c.ValueType == AttributeValueTypesDto.String)
        {
            var node = dataContext.Get<JsonNode>(c.Path);
            return ConvertNodeToString(node);
        }

        if (c.ValueType == AttributeValueTypesDto.Boolean)
        {
            var node = dataContext.Get<JsonNode>(c.Path);
            return ConvertNodeToBoolean(node, c);
        }

        if (c.ValueType == AttributeValueTypesDto.DateTime)
        {
            var node = dataContext.Get<JsonNode>(c.Path);
            return ConvertNodeToDateTime(node, c);
        }

        return c.ValueType switch
        {
            AttributeValueTypesDto.Int => (object?)dataContext.Get<int>(c.Path),
            AttributeValueTypesDto.Int64 => (object?)dataContext.Get<long>(c.Path),
            AttributeValueTypesDto.Double => (object?)dataContext.Get<double>(c.Path),
            _ => throw DataPipelineException.ValueTypeUnsupported(c.Path, c.ValueType)
        };
    }

    private static string? ConvertNodeToString(JsonNode? node)
    {
        if (node is null) return null;
        var kind = node.GetValueKind();
        return kind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.Number => node.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => node.ToJsonString()
        };
    }

    private static bool? ConvertNodeToBoolean(JsonNode? node, ConvertDataTypeNodeConfiguration c)
    {
        if (node is null) return null;
        return node.GetValueKind() switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => Convert.ToBoolean(node.GetValue<string>(), CultureInfo.InvariantCulture),
            JsonValueKind.Number => Convert.ToBoolean(node.GetValue<double>()),
            _ => throw DataPipelineException.ValueTypeUnsupported(c.Path, c.ValueType)
        };
    }

    private static DateTime? ConvertNodeToDateTime(JsonNode? node, ConvertDataTypeNodeConfiguration c)
    {
        if (node is null) return null;
        return node.GetValueKind() switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => Convert.ToDateTime(node.GetValue<string>(), CultureInfo.InvariantCulture),
            _ => throw DataPipelineException.ValueTypeUnsupported(c.Path, c.ValueType)
        };
    }
}
