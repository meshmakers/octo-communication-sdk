using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// Extracts a value from the string with a regular expression. Returns the capture group given by
    /// <see cref="TransformStringNodeConfiguration.GroupIndex"/> (default 1; 0 = whole match), or null when
    /// the pattern does not match. With <see cref="TransformStringNodeConfiguration.AsDecimal"/> the captured
    /// text is normalized (strip <see cref="TransformStringNodeConfiguration.GroupSeparator"/>, replace
    /// <see cref="TransformStringNodeConfiguration.DecimalSeparator"/> with '.') and written as a JSON number.
    /// </summary>
    RegexExtract = 8,
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

    /// <summary>
    /// The regular expression used by the <see cref="StringOperationDto.RegexExtract"/> operation.
    /// </summary>
    [PropertyGroup("Options", 3)]
    public string? Pattern { get; init; }

    /// <summary>
    /// The capture group returned by <see cref="StringOperationDto.RegexExtract"/> (0 = whole match). Defaults to 1.
    /// </summary>
    [PropertyGroup("Options", 4)]
    public int GroupIndex { get; init; } = 1;

    /// <summary>
    /// When true, the <see cref="StringOperationDto.RegexExtract"/> result is parsed as an invariant decimal and
    /// written as a JSON number instead of a string.
    /// </summary>
    [PropertyGroup("Options", 5)]
    public bool AsDecimal { get; init; }

    /// <summary>
    /// Digit-grouping separator stripped from the captured text before decimal parsing (e.g. "." for "1.234,56").
    /// </summary>
    [PropertyGroup("Options", 6)]
    public string? GroupSeparator { get; init; }

    /// <summary>
    /// Decimal separator in the captured text, replaced with '.' before invariant parsing (e.g. "," for "24,00").
    /// </summary>
    [PropertyGroup("Options", 7)]
    public string? DecimalSeparator { get; init; }
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
                matchCtx.Set(targetPath, result);
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

    private static JsonNode? ApplyStringOperation(string input, TransformStringNodeConfiguration config, INodeContext nodeContext)
    {
        return config.Operation switch
        {
            StringOperationDto.Trim => JsonValue.Create(input.Trim()),
            StringOperationDto.TrimStart => JsonValue.Create(input.TrimStart()),
            StringOperationDto.TrimEnd => JsonValue.Create(input.TrimEnd()),
            StringOperationDto.ToUpper => JsonValue.Create(input.ToUpper()),
            StringOperationDto.ToLower => JsonValue.Create(input.ToLower()),
            StringOperationDto.SubstringFromStart => JsonValue.Create(GetSubstringFromStart(input, config, nodeContext)),
            StringOperationDto.SubstringFromEnd => JsonValue.Create(GetSubstringFromEnd(input, config, nodeContext)),
            StringOperationDto.Substring => JsonValue.Create(GetSubstring(input, config, nodeContext)),
            StringOperationDto.RegexExtract => ApplyRegexExtract(input, config, nodeContext),
            _ => throw new NotSupportedException($"String operation {config.Operation} is not supported")
        };
    }

    /// <summary>
    /// Applies <see cref="TransformStringNodeConfiguration.Pattern"/> to the input and returns the requested
    /// capture group. Returns null when the pattern does not match (so a downstream filter simply finds no
    /// candidate). With <see cref="TransformStringNodeConfiguration.AsDecimal"/> the captured text is normalized
    /// and returned as a JSON number; if it does not parse, null is returned.
    /// </summary>
    private static JsonNode? ApplyRegexExtract(string input, TransformStringNodeConfiguration config, INodeContext nodeContext)
    {
        if (string.IsNullOrEmpty(config.Pattern))
        {
            nodeContext.Error("Pattern property is required for RegexExtract operation");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Pattern property is required for RegexExtract operation");
        }

        Match match;
        try
        {
            match = Regex.Match(input, config.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            nodeContext.Warning("RegexExtract pattern timed out on the input, returning null");
            return null;
        }

        if (!match.Success || config.GroupIndex < 0 || config.GroupIndex >= match.Groups.Count)
        {
            return null;
        }

        var group = match.Groups[config.GroupIndex];
        if (!group.Success)
        {
            return null;
        }

        if (!config.AsDecimal)
        {
            return JsonValue.Create(group.Value);
        }

        var normalized = group.Value;
        if (!string.IsNullOrEmpty(config.GroupSeparator))
        {
            normalized = normalized.Replace(config.GroupSeparator, string.Empty);
        }
        if (!string.IsNullOrEmpty(config.DecimalSeparator))
        {
            normalized = normalized.Replace(config.DecimalSeparator, ".");
        }

        return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? JsonValue.Create(number)
            : null;
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
