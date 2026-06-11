using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration of node for flattening an array of arrays into a one-dimensional array.
/// </summary>
[NodeName("Flatten", 1)]
public record FlattenNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <inheritdoc />
    public FlattenNodeConfiguration()
    {
        TargetValueKind = ValueKinds.Simple;
    }
}

/// <summary>
/// Flattening an array of arrays into a one-dimensional array.
/// </summary>
[NodeConfiguration(typeof(FlattenNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class FlattenNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<FlattenNodeConfiguration>();

        if (dataContext.GetKind("$") != DataKind.Undefined)
        {
            var collected = new JsonArray();
            await dataContext.IterateMatchesAsync(c.Path, matchCtx =>
            {
                var node = matchCtx.Get<JsonNode>("$");
                collected.Add(node?.DeepClone());
                return Task.CompletedTask;
            });

            dataContext.Set<JsonNode?>(c.TargetPath, collected,
                c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
        }

        await next(dataContext, nodeContext);
    }
}
