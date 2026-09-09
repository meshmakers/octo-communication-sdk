using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Defines how a join node treats a source object whose key has no match in the join array.
/// </summary>
public enum JoinNoMatchHandlingDto
{
    /// <summary>The source object receives an empty array at the item path and the run continues.</summary>
    Ignore = 0,
    /// <summary>The run fails with an exception naming the unmatched key, its path and the join path; an empty join array fails with an exception naming the join path.</summary>
    Fail = 1,
}

/// <summary>
/// Configuration for a join node that attaches to each object selected by <c>path</c> the array of
/// join records whose key matches the object's key (a nested left join: an object without a match
/// keeps an empty array). Elements at <c>path</c> that are not objects are skipped.
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

    /// <summary>
    /// Gets or sets whether a source object without a usable key (key path missing, null or empty)
    /// is kept as a left-join row with an empty array at <c>itemPath</c>. Off (default), such a
    /// source object fails the run; only with an empty join array and <c>noMatchHandling</c> Ignore
    /// is the key not read and every source object receives an empty array. Source objects with a
    /// key are joined as before either way.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public bool AllowMissingKey { get; set; }

    /// <summary>
    /// Gets or sets how a source object whose key has no match in the join array is handled
    /// (Ignore, the default, or Fail). A source object without a key is governed by
    /// <c>allowMissingKey</c>, not by this option. Under Fail an empty join array fails the run at
    /// the first source object, whatever its key.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public JoinNoMatchHandlingDto NoMatchHandling { get; set; } = JoinNoMatchHandlingDto.Ignore;
}

/// <summary>
/// A transformation node that attaches to each selected source object the join records whose key
/// matches the object's key, as an array at the item path (nested left join).
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

        var failOnNoMatch = c.NoMatchHandling == JoinNoMatchHandlingDto.Fail;
        var sourceMatchCount = 0;
        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            sourceMatchCount++;
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return Task.CompletedTask;
            }

            // Empty lookup. Ignore keeps the pre-existing behavior: every source object gets an
            // empty array without its key being read. Fail reports the lookup itself, which is the
            // better diagnosis than a mismatch of the first key.
            if (joinRecords.Count == 0)
            {
                if (failOnNoMatch)
                {
                    throw PipelineExecutionException.JoinPathHasNoRecords(nodeContext.NodePath, c.JoinPath);
                }

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
                if (!c.AllowMissingKey)
                {
                    throw PipelineExecutionException.ValueNotSet(nodeContext, c.KeyPath);
                }

                // Left join: the source object stays, with nothing joined to it.
                matchCtx.Set(itemPath, new JsonArray());
                return Task.CompletedTask;
            }

            var newArray = new JsonArray();
            foreach (var (joinValue, joinNode) in joinRecords)
            {
                if (joinValue == sourceValue && joinNode is not null)
                {
                    newArray.Add(joinNode.DeepClone());
                }
            }

            if (newArray.Count == 0 && failOnNoMatch)
            {
                // Only a scalar key goes into the message: an object-valued key (a misconfigured
                // keyPath) would put a whole document subtree into the error text, which reaches
                // logs and, under ForEach continueOnError with errorsPath, the pipeline document.
                var keyText = sourceKeyNode?.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array
                    ? "<non-scalar key>"
                    : sourceValue;
                throw PipelineExecutionException.JoinKeyNotMatched(nodeContext.NodePath, c.KeyPath, keyText,
                    c.JoinPath);
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
