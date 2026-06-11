using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Represents a string manipulation operation that can be performed on string data.
/// </summary>
public enum StringOperationDto
{
    /// <summary>Trims whitespace from both ends of the string.</summary>
    Trim = 0,
    /// <summary>Trims whitespace from the beginning (left side) of the string.</summary>
    TrimStart = 1,
    /// <summary>Trims whitespace from the end (right side) of the string.</summary>
    TrimEnd = 2,
    /// <summary>Converts the string to uppercase.</summary>
    ToUpper = 3,
    /// <summary>Converts the string to lowercase.</summary>
    ToLower = 4,
    /// <summary>Extracts a substring from the beginning of the string with specified length.</summary>
    SubstringFromStart = 5,
    /// <summary>Extracts a substring from the end of the string with specified length.</summary>
    SubstringFromEnd = 6,
    /// <summary>Extracts a substring starting at a specific position with optional length.</summary>
    Substring = 7,
}

/// <summary>
/// Configuration for a string manipulation transformation node that performs various string operations.
/// </summary>
[NodeName("TransformString", 1)]
public record TransformStringNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the relative path to the source string value within the selected objects.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public required string SourcePath { get; init; }

    /// <summary>
    /// Specifies the string manipulation operation to be performed on the data.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required StringOperationDto Operation { get; init; }

    /// <summary>
    /// The starting position for substring operations (Substring).
    /// </summary>
    [PropertyGroup("Options", 1)]
    public int StartIndex { get; init; } = 0;

    /// <summary>
    /// The length of the substring to extract.
    /// </summary>
    [PropertyGroup("Options", 2)]
    public int? Length { get; init; }
}

/// <summary>
/// A transformation node that performs various string manipulation operations on string values.
/// </summary>
[NodeConfiguration(typeof(TransformStringNodeConfiguration))]
public class TransformStringNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<TransformStringNodeConfiguration>();

        if (dataContext.GetKind("$") == DataKind.Null || dataContext.GetKind("$") == DataKind.Undefined)
        {
            throw PipelineExecutionException.InputValueNull(nodeContext);
        }

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
                var sourceValue = sourceTokenValue.GetValueKind() == JsonValueKind.String
                    ? sourceTokenValue.GetValue<string>()
                    : sourceTokenValue.ToJsonString();
                var result = ApplyStringOperation(sourceValue, c, nodeContext);
                matchCtx.Set(targetPath, JsonValue.Create(result));
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

    private static string ApplyStringOperation(string input, TransformStringNodeConfiguration config, INodeContext nodeContext)
    {
        return config.Operation switch
        {
            StringOperationDto.Trim => input.Trim(),
            StringOperationDto.TrimStart => input.TrimStart(),
            StringOperationDto.TrimEnd => input.TrimEnd(),
            StringOperationDto.ToUpper => input.ToUpper(),
            StringOperationDto.ToLower => input.ToLower(),
            StringOperationDto.SubstringFromStart => GetSubstringFromStart(input, config, nodeContext),
            StringOperationDto.SubstringFromEnd => GetSubstringFromEnd(input, config, nodeContext),
            StringOperationDto.Substring => GetSubstring(input, config, nodeContext),
            _ => throw new NotSupportedException($"String operation {config.Operation} is not supported")
        };
    }

    private static string GetSubstringFromStart(string input, TransformStringNodeConfiguration config, INodeContext nodeContext)
    {
        if (!config.Length.HasValue)
        {
            nodeContext.Error("Length property is required for SubstringFromStart operation");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Length property is required for SubstringFromStart operation");
        }

        var length = Math.Min(config.Length.Value, input.Length);
        if (length <= 0)
        {
            return string.Empty;
        }

        return input.Substring(0, length);
    }

    private static string GetSubstringFromEnd(string input, TransformStringNodeConfiguration config, INodeContext nodeContext)
    {
        if (!config.Length.HasValue)
        {
            nodeContext.Error("Length property is required for SubstringFromEnd operation");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Length property is required for SubstringFromEnd operation");
        }

        var length = Math.Min(config.Length.Value, input.Length);
        if (length <= 0)
        {
            return string.Empty;
        }

        var startIndex = input.Length - length;
        return input.Substring(startIndex, length);
    }

    private static string GetSubstring(string input, TransformStringNodeConfiguration config, INodeContext nodeContext)
    {
        if (config.StartIndex < 0 || config.StartIndex >= input.Length)
        {
            nodeContext.Warning("StartIndex {0} is out of bounds for string of length {1}, returning empty string", config.StartIndex, input.Length);
            return string.Empty;
        }

        if (!config.Length.HasValue)
        {
            return input.Substring(config.StartIndex);
        }

        var availableLength = input.Length - config.StartIndex;
        var length = Math.Min(config.Length.Value, availableLength);

        if (length <= 0)
        {
            return string.Empty;
        }

        return input.Substring(config.StartIndex, length);
    }
}
