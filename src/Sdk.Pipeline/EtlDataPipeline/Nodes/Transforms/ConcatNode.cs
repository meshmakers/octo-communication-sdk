using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Internal;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration data type conversion
/// </summary>
[NodeName("Concat", 1)]
public record ConcatNodeConfiguration : PathNodeConfiguration
{
    /// <summary>
    /// Data type that the value is cast to during transformation
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [PropertyGroup("Data", 0)]
    public required List<ConcatItem> Parts { get; set; }

    /// <summary>
    /// Defines the path of the concatenated string. The path is relative to the select Path
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public required string ConcatSubPath { get; set; }
}

/// <summary>
/// Represents an item to concat.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record ConcatItem
{
    /// <summary>
    /// The value to concat. Either this or <see cref="ValuePath"/> must be set.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The value path to concat. Either this or <see cref="Value"/> must be set.
    /// </summary>
    public string? ValuePath { get; set; }
}

/// <summary>
/// Concatenates the values to a single string
/// </summary>
[NodeConfiguration(typeof(ConcatNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class ConcatNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ConcatNodeConfiguration>();

        var concatSubPath = JsonNodePath.NormalizePathOrRelative(c.ConcatSubPath);
        // Pre-normalize value paths once; bare paths get a "$." prefix.
        var normalizedParts = c.Parts.Select(p => new
        {
            p.Value,
            ValuePath = string.IsNullOrEmpty(p.ValuePath) ? null : JsonNodePath.NormalizePathOrRelative(p.ValuePath!)
        }).ToList();

        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return Task.CompletedTask;
            }

            var value = string.Join("", normalizedParts.Select(p =>
            {
                if (p.Value != null) return p.Value;
                if (p.ValuePath is null) return string.Empty;
                var found = matchCtx.Get<JsonNode>(p.ValuePath);
                if (found is null) return string.Empty;
                return JsonStringifyHelper.ToLegacyString(found) ?? string.Empty;
            }));

            matchCtx.Set(concatSubPath, JsonValue.Create(value));
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        await next(dataContext, nodeContext);
    }

}
