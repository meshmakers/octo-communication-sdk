using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for removing duplicates from an array.
/// Supports scalar arrays (strings, numbers, etc.) and object arrays (deduplicated by a property).
/// </summary>
[NodeName("Distinct", 1)]
public record DistinctNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Optional path to the value that defines uniqueness within object arrays.
    /// When not set, the node deduplicates scalar values directly.
    /// When set, each array item must be an object with a property at this path
    /// of a simple type (int, double, string, bool, datetime).
    /// </summary>
    [PropertyGroup("Query", 0, "jsonpath")]
    public string? DistinctValuePath { get; set; }
}

/// <summary>
/// Removes duplicates from an array. Supports both scalar arrays and object arrays.
/// </summary>
[NodeConfiguration(typeof(DistinctNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class DistinctNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<DistinctNodeConfiguration>();

        if (dataContext.GetKind(c.Path) != DataKind.Array)
        {
            await next(dataContext, nodeContext);
            return;
        }

        var sourceArray = dataContext.Get<JsonArray>(c.Path);
        if (sourceArray is null || sourceArray.Count == 0)
        {
            await next(dataContext, nodeContext);
            return;
        }

        var seenValues = new HashSet<object>();
        var distinctItems = new List<JsonNode?>();

        foreach (var item in sourceArray)
        {
            JsonNode? uniqueNode;
            if (string.IsNullOrWhiteSpace(c.DistinctValuePath))
            {
                // Scalar array — use the item itself as uniqueness key
                uniqueNode = item is JsonValue ? item : null;
            }
            else if (item is JsonObject jObject)
            {
                // Object array — deduplicate by property at DistinctValuePath
                uniqueNode = JsonPathWalker.Select(new NodeView(jObject), c.DistinctValuePath!)
                    .Select(m => m.Match.Node)
                    .FirstOrDefault();
            }
            else
            {
                continue;
            }

            if (uniqueNode is null)
            {
                continue;
            }

            var uniqueValue = ConvertNodeToValue(uniqueNode);
            if (uniqueValue is not null && seenValues.Add(uniqueValue))
            {
                distinctItems.Add(item?.DeepClone());
            }
        }

        var resultArray = new JsonArray();
        foreach (var item in distinctItems)
        {
            resultArray.Add(item);
        }

        dataContext.Set(c.TargetPath, resultArray, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }

    private static object? ConvertNodeToValue(JsonNode node)
    {
        if (node is JsonValue jv)
        {
            // parseDateStrings:false — strings stay strings so "2024-01-01" and
            // "2024-01-01T00:00:00" are distinct keys rather than collapsing to the
            // same DateTime. Date-typed equality is opt-in via an explicit ConvertDataType
            // node upstream, not automatic here.
            return JsonScalar.ToClr(jv.GetValue<JsonElement>(), parseDateStrings: false);
        }

        // Fall back to canonical JSON string for non-JsonValue nodes (won't normally occur for scalar dedup,
        // but keeps the equality semantics safe for edge cases).
        return node.ToJsonString();
    }
}
