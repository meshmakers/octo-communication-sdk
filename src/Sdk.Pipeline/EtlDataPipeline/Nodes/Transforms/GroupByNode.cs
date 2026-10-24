using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for grouping the items of an array by a key.
/// Not to be confused with <c>Group@1</c>, which is a structural container of the pipeline editor
/// and has no runtime effect.
/// </summary>
[NodeName("GroupBy", 1)]
public record GroupByNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Optional path, relative to each array item, to the value that defines the group
    /// (a simple value: string, number, boolean or date string).
    /// When not set, the array must contain scalar values and each value is its own key.
    /// </summary>
    [PropertyGroup("Query", 0, "jsonpath")]
    public string? KeyPath { get; set; }
}

/// <summary>
/// Groups the items of an array by a key. The result is an array of <c>{ "key": ..., "items": [...] }</c>
/// objects, ordered by the first occurrence of each key; items keep their original order within a group.
/// Items without a usable key are skipped and reported in a single warning per run.
/// </summary>
[NodeConfiguration(typeof(GroupByNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class GroupByNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<GroupByNodeConfiguration>();

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

        // Index by normalised key value: O(n) regardless of the number of groups.
        var groupIndex = new Dictionary<object, int>();
        var groups = new List<(JsonNode Key, JsonArray Items)>();
        var skipped = 0;

        foreach (var item in sourceArray)
        {
            var keyNode = SelectKeyNode(item, c.KeyPath);
            var keyValue = keyNode is JsonValue jv ? Normalize(jv) : null;
            if (keyNode is null || keyValue is null)
            {
                skipped++;
                continue;
            }

            if (!groupIndex.TryGetValue(keyValue, out var index))
            {
                index = groups.Count;
                groupIndex.Add(keyValue, index);
                groups.Add((keyNode.DeepClone(), new JsonArray()));
            }

            groups[index].Items.Add(item?.DeepClone());
        }

        if (skipped > 0)
        {
            nodeContext.Warning(
                "GroupBy skipped {0} of {1} items without a simple key value at '{2}'",
                skipped, sourceArray.Count, c.KeyPath ?? "(item value)");
        }

        var resultArray = new JsonArray();
        foreach (var (key, items) in groups)
        {
            resultArray.Add(new JsonObject
            {
                ["key"] = key,
                ["items"] = items
            });
        }

        dataContext.Set(c.TargetPath, resultArray, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }

    private static JsonNode? SelectKeyNode(JsonNode? item, string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            // Scalar array — the item itself is the key
            return item is JsonValue ? item : null;
        }

        if (item is not JsonObject jObject)
        {
            return null;
        }

        return JsonPathWalker.Select(new NodeView(jObject), keyPath)
            .Select(m => m.Match.Node)
            .FirstOrDefault();
    }

    /// <summary>
    /// Value-based key. Integral numbers collapse to <see cref="long"/> so that <c>1</c> and <c>1.0</c>
    /// are the same key. Strings stay strings (ordinal comparison, date-like strings are not parsed).
    /// </summary>
    private static object? Normalize(JsonValue value)
    {
        var clr = JsonScalar.ToClr(value, parseDateStrings: false);
        return clr switch
        {
            int i => (long)i,
            double d when d == Math.Floor(d) && d >= long.MinValue && d < long.MaxValue => (long)d,
            _ => clr
        };
    }
}
