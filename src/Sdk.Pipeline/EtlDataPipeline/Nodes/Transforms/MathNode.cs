using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Represents a mathematical operation that can be performed on data.
/// </summary>
public enum MathOperationDto
{
    /// <summary>Specifies a multiplication operation.</summary>
    Multiply = 0,
    /// <summary>Specifies a division operation.</summary>
    Divide = 1,
    /// <summary>Specifies an addition operation.</summary>
    Add = 2,
    /// <summary>Specifies a subtraction operation.</summary>
    Subtract = 3,
    /// <summary>Specifies a modulo operation that returns the remainder of a division.</summary>
    Modulo = 4,
    /// <summary>Specifies a rounding operation that rounds a number to a specified number of decimal places.</summary>
    Round = 5,
}

/// <summary>
/// Represents a configuration for a math node that performs mathematical operations on data.
/// </summary>
[NodeName("Math", 1)]
public record MathNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Specifies the mathematical operation to be performed on the data.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required MathOperationDto Operation { get; init; }

    /// <summary>
    /// The second value to be used in the mathematical operation.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public double? Value { get; init; }

    /// <summary>
    /// The path to the value to be used in the mathematical operation.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string? ValuePath { get; init; }

    /// <summary>
    /// Relative path to the source objects where the value to be processed is located.
    /// </summary>
    [PropertyGroup("Paths", 3, "jsonpath")]
    public required string ItemPath { get; init; }

    /// <summary>
    /// Relative path to the source objects where the result of the operation will be stored.
    /// </summary>
    [PropertyGroup("Paths", 4, "jsonpath")]
    public required string ItemTargetPath { get; init; } = "$.Result";

    /// <summary>
    /// The number of decimal places to round to when using the Round operation.
    /// </summary>
    [PropertyGroup("Options", 2)]
    public int DecimalPlaces { get; init; } = 0;
}

/// <summary>
/// Math node that performs mathematical operations on data.
/// </summary>
[NodeConfiguration(typeof(MathNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class MathNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<MathNodeConfiguration>();

        if (dataContext.GetKind("$") == DataKind.Null || dataContext.GetKind("$") == DataKind.Undefined)
        {
            throw PipelineExecutionException.InputValueNull(nodeContext);
        }

        // For Round operation, we don't need a value since we use DecimalPlaces
        var value = c.Operation == MathOperationDto.Round ? 0 : GetValue(dataContext, c);
        if (value == null && c.Operation != MathOperationDto.Round)
        {
            throw PipelineExecutionException.ValueNotSet(nodeContext, c.ValuePath);
        }

        var itemPath = JsonNodePath.NormalizePathOrRelative(c.ItemPath);
        var itemTargetPath = JsonNodePath.NormalizePathOrRelative(c.ItemTargetPath);

        var matchCount = 0;
        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            matchCount++;
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return Task.CompletedTask;
            }

            var itemNode = matchCtx.Get<JsonNode>(itemPath);
            if (itemNode is null)
            {
                nodeContext.Warning("No numeric value found at path '{0}'", c.Path);
                return Task.CompletedTask;
            }

            // Shared single-source numeric read (JsonScalar.TryToDouble): JSON numbers
            // natively, numeric JSON strings parsed under invariant culture
            // (NumberStyles.Float | AllowThousands); anything else is skipped. This replaces
            // the hand-rolled number-or-parse-string ladder so the parity rules live in one place.
            if (!JsonScalar.TryToDouble(itemNode, out var sourceValue))
            {
                nodeContext.Warning("No numeric value found at path '{0}'", c.Path);
                return Task.CompletedTask;
            }

            var result = c.Operation switch
            {
                MathOperationDto.Add => sourceValue + value,
                MathOperationDto.Subtract => sourceValue - value,
                MathOperationDto.Multiply => sourceValue * value,
                MathOperationDto.Divide => sourceValue / value,
                MathOperationDto.Modulo => sourceValue % value,
                MathOperationDto.Round => Math.Round(sourceValue, c.DecimalPlaces),
                _ => throw new NotSupportedException($"Operation {c.Operation} is not supported")
            };

            matchCtx.Set(itemTargetPath, JsonValue.Create(result));
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        if (matchCount == 0)
        {
            nodeContext.Warning("No source data found at path '{0}'", c.Path);
            return;
        }

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }

    private static double? GetValue(IDataContext dataContext, MathNodeConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.ValuePath))
        {
            return dataContext.Get<double?>(config.ValuePath!);
        }

        return config.Value;
    }
}
