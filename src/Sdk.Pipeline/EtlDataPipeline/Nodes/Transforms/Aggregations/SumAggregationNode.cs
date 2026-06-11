using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Aggregations;

/// <summary>
/// Configuration for a sum aggregation node that calculates weighted sums from multiple data sources with optional filtering.
/// </summary>
[NodeName("SumAggregation", 1)]
public record SumAggregationNodeConfiguration : TargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the collection of aggregation items.
    /// </summary>
    [PropertyGroup("Data Mapping", 0)]
    public required IEnumerable<SumAggregationItem> Aggregations { get; init; }
}

/// <summary>
/// Represents a single data source configuration for sum aggregation.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record SumAggregationItem
{
    /// <summary>JSONPath that selects the container objects to process.</summary>
    public required string Path { get; init; }

    /// <summary>Optional JSONPath used to filter selected objects via comparison with <see cref="ComparisonValue"/>.</summary>
    public required string? FilterPath { get; init; }

    /// <summary>Comparison value used by FilterPath equality test.</summary>
    public required object? ComparisonValue { get; init; }

    /// <summary>JSONPath relative to a filtered object that yields numeric values.</summary>
    public required string AggregationPath { get; init; }

    /// <summary>Multiplier applied to each extracted numeric value before summation.</summary>
    public required double Value { get; init; }
}


/// <summary>
/// A transformation node that performs weighted sum aggregations across multiple filtered data sources.
/// </summary>
[NodeConfiguration(typeof(SumAggregationNodeConfiguration))]
public class SumAggregationNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<SumAggregationNodeConfiguration>();

        var root = dataContext.Get<JsonNode>("$");
        if (root is null)
        {
            throw PipelineExecutionException.InputValueNull(nodeContext);
        }

        // Use a snapshot JsonDocument for read-only JSONPath evaluation.
        using var doc = JsonDocument.Parse(root.ToJsonString());

        double d = 0.0;
        foreach (var item in c.Aggregations)
        {
            var sourceMatches = JsonPathWalker.Select(new ElementView(doc.RootElement), item.Path);

            foreach (var (sourceView, _) in sourceMatches)
            {
                if (!string.IsNullOrWhiteSpace(item.FilterPath) && item.FilterPath != null)
                {
                    var filterMatches = JsonPathWalker
                        .Select(sourceView, item.FilterPath)
                        .ToList();
                    var use = filterMatches.All(s => GetElementAsString(s.Match.Element) == item.ComparisonValue?.ToString());
                    if (!use)
                    {
                        continue;
                    }
                }

                foreach (var (aggView, _) in JsonPathWalker.Select(sourceView, item.AggregationPath))
                {
                    if (!TryGetElementAsDouble(aggView.Element, out var v))
                    {
                        throw new PipelineExecutionException(
                            $"[{nodeContext.NodePath}]: Value at '{item.AggregationPath}' is not numeric (got {aggView.Element.ValueKind})");
                    }
                    d += v * item.Value;
                }
            }
        }

        dataContext.Set(c.TargetPath, d, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }

    private static string GetElementAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };
    }

    private static bool TryGetElementAsDouble(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDouble(out value)) return true;
                break;
            case JsonValueKind.String:
                return double.TryParse(element.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        value = 0;
        return false;
    }
}
