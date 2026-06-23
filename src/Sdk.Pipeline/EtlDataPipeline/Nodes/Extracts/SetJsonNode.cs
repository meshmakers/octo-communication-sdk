using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;

/// <summary>
/// Configuration for the WriteJsonNode
/// </summary>
[NodeName("WriteJson", 1)]
public record SetJsonNodeConfiguration : TargetPathNodeConfiguration
{
    /// <summary>
    /// The JSON string to write to the current object
    /// </summary>
    [PropertyGroup("Data", 0)]
    public string? JsonString { get; init; }
    /// <summary>
    /// The JSON path to a JSON string to write to the target path
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string? JsonStringPath { get; init; }
}

/// <summary>
/// Sets a JSON string to the current object
/// </summary>
/// <param name="next">Next node in the pipeline</param>
[NodeConfiguration(typeof(SetJsonNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class SetJsonNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<SetJsonNodeConfiguration>();

        if (c.JsonString != null)
        {
            dataContext.Set<JsonNode?>(c.TargetPath, JsonNode.Parse(c.JsonString),
                c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
        }
        else if (c.JsonStringPath != null)
        {
            var jsonString = dataContext.Get<string>(c.JsonStringPath);
            if (jsonString != null)
            {
                dataContext.Set<JsonNode?>(c.TargetPath, JsonNode.Parse(jsonString),
                    c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
            }
        }

        return next(dataContext, nodeContext);
    }
}
