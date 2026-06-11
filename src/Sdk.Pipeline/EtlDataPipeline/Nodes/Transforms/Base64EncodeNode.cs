using System.Text;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Internal;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for a Base64 encoding transformation node that converts string values to Base64 encoded strings.
/// </summary>
[NodeName("Base64Encode", 1)]
public record Base64EncodeNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the relative path to the source value within the selected objects.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public required string SourcePath { get; init; }
}

/// <summary>
/// A transformation node that encodes string values to Base64 format.
/// </summary>
[NodeConfiguration(typeof(Base64EncodeNodeConfiguration))]
public class Base64EncodeNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<Base64EncodeNodeConfiguration>();

        var sourcePath = JsonNodePath.NormalizePathOrRelative(c.SourcePath);
        var targetPath = JsonNodePath.NormalizePathOrRelative(c.TargetPath);

        var matchCount = 0;
        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            matchCount++;
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return Task.CompletedTask;
            }

            var sourceTokenValue = matchCtx.Get<JsonNode>(sourcePath);
            if (sourceTokenValue is not null)
            {
                var sourceValue = JsonStringifyHelper.ToLegacyString(sourceTokenValue) ?? string.Empty;
                var bytes = Encoding.UTF8.GetBytes(sourceValue);
                var encodedValue = Convert.ToBase64String(bytes);
                matchCtx.Set(targetPath, JsonValue.Create(encodedValue));
            }
            else
            {
                matchCtx.Set<JsonNode?>(targetPath, null);
            }

            return Task.CompletedTask;
        }).ConfigureAwait(false);

        if (matchCount == 0)
        {
            nodeContext.Warning("No source data found at path '{0}'", c.Path);
            return;
        }

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }

}
