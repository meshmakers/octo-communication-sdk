using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for a Base64 decoding transformation node that converts Base64 encoded strings back to their original string values.
/// </summary>
[NodeName("Base64Decode", 1)]
public record Base64DecodeNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the relative path to the Base64 encoded value within the selected objects.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public required string SourcePath { get; init; }
}

/// <summary>
/// A transformation node that decodes Base64 encoded strings back to their original string values.
/// </summary>
[NodeConfiguration(typeof(Base64DecodeNodeConfiguration))]
public class Base64DecodeNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<Base64DecodeNodeConfiguration>();

        var sourcePath = JsonNodePath.NormalizePathOrRelative(c.SourcePath);
        var targetPath = JsonNodePath.NormalizePathOrRelative(c.TargetPath);

        var matchCount = 0;
        await dataContext.UpdateMatchesAsync(c.Path, async matchCtx =>
        {
            matchCount++;
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return;
            }

            var sourceTokenValue = matchCtx.Get<JsonNode>(sourcePath);
            if (sourceTokenValue is not null)
            {
                var sourceValue = sourceTokenValue.GetValueKind() == JsonValueKind.String
                    ? sourceTokenValue.GetValue<string>()
                    : sourceTokenValue.ToJsonString();
                try
                {
                    var bytes = Convert.FromBase64String(sourceValue);
                    var decodedValue = Encoding.UTF8.GetString(bytes);
                    matchCtx.Set(targetPath, JsonValue.Create(decodedValue));
                }
                catch (FormatException ex)
                {
                    nodeContext.Error("Failed to decode Base64 value at path '{0}.{1}': {2}",
                        c.Path, c.SourcePath, ex.Message);
                    throw;
                }
            }
            else
            {
                matchCtx.Set<JsonNode?>(targetPath, null);
            }

            await Task.CompletedTask;
        }).ConfigureAwait(false);

        if (matchCount == 0)
        {
            nodeContext.Warning("No source data found at path '{0}'", c.Path);
            return;
        }

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }

}
