using System.Collections.Generic;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration of node for pivot transformation, converting a JSON object with multiple columns into a list of objects with a single column.
/// </summary>
/// <remarks>
/// This type of transformation is known as a pivot transformation or data restructuring, specifically a conversion from wide format to long format, where the JSON object, currently organized column-wise, is transformed into a list format with each row (based on the timestamp) as a separate object.
/// </remarks>
[NodeName("Map", 1)]
public record MapNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <inheritdoc />
    public MapNodeConfiguration()
    {
        TargetValueKind = ValueKinds.Simple;
    }

    /// <summary>
    /// Properties to transform
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public IEnumerable<string> SelectPaths { get; set; } = new List<string>();
}

/// <summary>
/// Mapping/Pivot transformation, converting a JSON object with multiple columns into a list of objects with a single column.
/// </summary>
[NodeConfiguration(typeof(MapNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class MapNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<MapNodeConfiguration>();

        var transformedData = new List<JsonObject>();
        var data = dataContext.Get<JsonNode>(c.Path);
        if (data is not null)
        {
            // Find the maximum count of all the paths
            var count = 0;
            var pathData = new Dictionary<string, JsonArray>();
            foreach (var path in c.SelectPaths)
            {
                var selectNode = JsonPathWalker.Select(new NodeView(data), path)
                    .Select(m => m.Match.Node)
                    .FirstOrDefault();

                if (selectNode is JsonArray array)
                {
                    count = System.Math.Max(count, array.Count);
                    pathData[path] = array;
                }
                else
                {
                    nodeContext.Warning($"Path '{path}' is not an array and will be skipped.");
                }
            }

            for (var i = 0; i < count; i++)
            {
                var entry = new JsonObject();

                foreach (var keyValue in pathData)
                {
                    var sourceItem = keyValue.Value.Count > i ? keyValue.Value[i] : null;
                    JsonNodePath.Set(entry, keyValue.Key, sourceItem);
                }

                transformedData.Add(entry);
            }
        }

        var target = new JsonArray();
        foreach (var entry in transformedData)
        {
            target.Add(entry);
        }

        dataContext.Set(c.TargetPath, target, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }
}
