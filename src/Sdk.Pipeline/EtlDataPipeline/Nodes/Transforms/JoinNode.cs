using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for a join node that performs inner joins between source data and lookup arrays.
/// </summary>
[NodeName("Join", 1)]
public record JoinNodeConfiguration : PathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the JSONPath to the key field in the source data that will be used for matching.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public required string KeyPath { get; set; }

    /// <summary>
    /// Gets or sets the JSONPath to the array of items that will be used as the lookup/join source.
    /// </summary>
    [PropertyGroup("Paths", 3, "jsonpath")]
    public required string JoinPath { get; set; }

    /// <summary>
    /// Gets or sets the JSONPath to the key field in the join array items that will be matched against the source key.
    /// </summary>
    [PropertyGroup("Paths", 4, "jsonpath")]
    public required string JoinKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the JSONPath where the array of matched join records will be stored in the source data.
    /// </summary>
    [PropertyGroup("Paths", 5, "jsonpath")]
    public required string ItemPath { get; set; }
}

/// <summary>
/// A transformation node that performs inner join operations between source data and lookup arrays based on matching key values.
/// </summary>
[NodeConfiguration(typeof(JoinNodeConfiguration))]
public class JoinNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<JoinNodeConfiguration>();

        if (dataContext.GetKind("$") == DataKind.Null || dataContext.GetKind("$") == DataKind.Undefined)
        {
            throw PipelineExecutionException.InputValueNull(nodeContext);
        }

        var keyPath = JsonNodePath.NormalizePathOrRelative(c.KeyPath);
        var joinKeyPath = JsonNodePath.NormalizePathOrRelative(c.JoinKeyPath);
        var itemPath = JsonNodePath.NormalizePathOrRelative(c.ItemPath);

        // Resolve join records once via SelectMatches; each yielded IDataContext is rooted at a
        // freshly materialized copy of the match node, independent of the source document.
        // Pre-extract the join key value for cheap comparison during the source pass.
        var joinRecords = new List<(string? Key, JsonNode? Node)>();
        foreach (var joinCtx in dataContext.SelectMatches(c.JoinPath))
        {
            using (joinCtx)
            {
                // Resolve the join-side key with the full JSONPath dialect — the same resolver the
                // source side uses below (matchCtx.Get) — so bracket/index/wildcard selectors in
                // JoinKeyPath (e.g. "$.keys[0]") match instead of silently returning null.
                var joinKey = joinCtx.Get<JsonNode>(joinKeyPath);
                var joinValue = joinKey is null
                    ? null
                    : (joinKey.GetValueKind() == JsonValueKind.String
                        ? joinKey.GetValue<string>()
                        : joinKey.ToJsonString());
                // Snapshot the full record as a JsonNode before disposing the context.
                var joinNode = joinCtx.Get<JsonNode>("$");
                joinRecords.Add((joinValue, joinNode));
            }
        }

        var sourceMatchCount = 0;
        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            sourceMatchCount++;
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return Task.CompletedTask;
            }

            if (joinRecords.Count == 0)
            {
                matchCtx.Set(itemPath, new JsonArray());
                return Task.CompletedTask;
            }

            var sourceKeyNode = matchCtx.Get<JsonNode>(keyPath);
            var sourceValue = sourceKeyNode is null
                ? null
                : (sourceKeyNode.GetValueKind() == JsonValueKind.String
                    ? sourceKeyNode.GetValue<string>()
                    : sourceKeyNode.ToJsonString());
            if (string.IsNullOrEmpty(sourceValue))
            {
                throw PipelineExecutionException.ValueNotSet(nodeContext, c.KeyPath);
            }

            var newArray = new JsonArray();
            foreach (var (joinValue, joinNode) in joinRecords)
            {
                if (joinValue == sourceValue && joinNode is not null)
                {
                    newArray.Add(joinNode.DeepClone());
                }
            }
            matchCtx.Set(itemPath, newArray);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        if (sourceMatchCount == 0)
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, c.Path);
        }

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }
}
