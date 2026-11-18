using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for node logger: writes one log line per execution of the node, with a static
/// message or a message read from the data context, at a chosen level and with structured
/// attributes.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one of <see cref="Message" /> and <see cref="MessagePath" /> must be set. The log body is
/// <c>&lt;nodePath&gt;: &lt;message&gt;</c> like every other pipeline log line; the message is logged as
/// a value, so braces such as <c>{0}</c> are kept verbatim. Every record carries the attributes
/// <c>TenantId</c>, <c>PipelineRtId</c> and <c>NodePath</c> plus the configured
/// <see cref="Attributes" />.
/// </para>
/// <para>
/// Missing data never fails the node: a missing or null value is logged as <see cref="MissingValue" />.
/// Values registered as secret in the execution are masked. The message is limited to
/// <see cref="MaxMessageLength" /> characters and every attribute value to
/// <see cref="MaxAttributeValueLength" />; a longer text is cut and ends with
/// <see cref="TruncationMarker" />. At most
/// <see cref="MaxAttributes" /> attributes are allowed. Do not log personal or secret data.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// - type: FormatString@1
///   format: "EDA message skipped for {$.meteringPoint}"
///   targetPath: $.logText
/// - type: Logger@1
///   messagePath: $.logText
///   level: Warning
///   attributes:
///     - name: MeteringPoint
///       valuePath: $.meteringPoint
///     - name: Reason
///       value: UnknownMeteringPoint
/// </code>
/// </example>
[NodeName("Logger", 1)]
public record LoggerNodeConfiguration : NodeConfiguration
{
    /// <summary>The value logged for a missing or null message or attribute value unless <see cref="MissingValue" /> is set.</summary>
    public const string DefaultMissingValue = "<missing>";

    /// <summary>Text appended to a message or attribute value that was cut to its maximum length.</summary>
    public const string TruncationMarker = "...[truncated]";

    /// <summary>Maximum number of characters of the message (2 KB); longer messages are truncated.</summary>
    public const int MaxMessageLength = 2048;

    /// <summary>Maximum number of characters of an attribute value; longer values are truncated.</summary>
    public const int MaxAttributeValueLength = 256;

    /// <summary>Maximum number of configured attributes; more is a configuration error.</summary>
    public const int MaxAttributes = 20;

    /// <summary>
    /// Static message to log. Either this or <c>messagePath</c> must be set, not both. Logged
    /// verbatim: braces are not placeholders.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public string? Message { get; init; }

    /// <summary>
    /// JSONPath to the message in the data context, e.g. a text built by FormatString@1. Either this
    /// or <c>message</c> must be set, not both. A string is logged as it is, any other value as
    /// compact JSON; a missing or null value as <c>missingValue</c>.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string? MessagePath { get; init; }

    /// <summary>
    /// Log level of the line: Debug, Information, Warning or Error. Default: Information.
    /// </summary>
    // DefaultValue: the YAML serializer omits default values, and without it YamlDotNet takes the
    // enum's zero value (Debug) as the default - a Debug node would be saved without its level and
    // reloaded as Information.
    [PropertyGroup("Options", 1)]
    [DefaultValue(LoggerNodeLevel.Information)]
    public LoggerNodeLevel Level { get; init; } = LoggerNodeLevel.Information;

    /// <summary>
    /// Structured attributes of the log line, at most 20. Each has a <c>name</c> and either a static
    /// <c>value</c> or a <c>valuePath</c> into the data context (<c>valuePath</c> wins when both are
    /// set). Values are limited to 256 characters. Names are unique (ignoring case); TenantId,
    /// PipelineRtId and NodePath are set by the node, and the fields of the JSON log line (timestamp,
    /// level, logger, callsite, message, trace_id, span_id, exception.*) and names starting with "{" cannot be used either.
    /// </summary>
    [PropertyGroup("Options", 2)]
    [MaxLength(MaxAttributes)]
    public List<LoggerNodeAttribute>? Attributes { get; init; }

    /// <summary>
    /// Text logged for a missing or null message or attribute value. Default: <c>&lt;missing&gt;</c>.
    /// </summary>
    [PropertyGroup("Options", 3)]
    public string? MissingValue { get; init; }
}

/// <summary>
/// Log level of a <c>Logger@1</c> line.
/// </summary>
public enum LoggerNodeLevel
{
    /// <summary>Debug severity</summary>
    Debug = 0,

    /// <summary>Information severity (default)</summary>
    Information = 1,

    /// <summary>Warning severity</summary>
    Warning = 2,

    /// <summary>Error severity</summary>
    Error = 3
}

/// <summary>
/// A structured attribute of a <c>Logger@1</c> line.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record LoggerNodeAttribute
{
    /// <summary>
    /// Name of the attribute, e.g. <c>MeteringPoint</c>. Must be unique within the node.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Static value of the attribute. Either this or <c>valuePath</c> must be set.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// JSONPath to the attribute value in the data context; takes precedence over <c>value</c>. A
    /// string is logged as it is, any other value as compact JSON; a missing or null value as
    /// <c>missingValue</c>.
    /// </summary>
    public string? ValuePath { get; init; }
}

/// <summary>
/// Logs a message
/// </summary>
/// <param name="next">The next node</param>
[NodeConfiguration(typeof(LoggerNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
internal class LoggerNode(NodeDelegate next) : IPipelineNode
{
    internal const string TenantIdAttribute = "TenantId";
    internal const string PipelineRtIdAttribute = "PipelineRtId";
    internal const string NodePathAttribute = "NodePath";

    /// <summary>
    /// Names the node sets itself, plus the fields of the adapters' JSON log layout: an attribute with
    /// one of these names would duplicate a field of the log line.
    /// </summary>
    private static readonly HashSet<string> ReservedAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        TenantIdAttribute, PipelineRtIdAttribute, NodePathAttribute,
        "timestamp", "level", "logger", "callsite", "message", "trace_id", "span_id",
        "exception.type", "exception.message", "exception.stacktrace"
    };

    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<LoggerNodeConfiguration>();
        Validate(c, nodeContext);

        var missingValue = Redact(nodeContext, c.MissingValue ?? LoggerNodeConfiguration.DefaultMissingValue);

        // Redact before cutting, so a secret across the cut-off point is still masked as a whole.
        var message = c.Message != null
            ? Redact(nodeContext, c.Message)
            : ReadValue(dataContext, nodeContext, c.MessagePath!, missingValue);
        message = Limit(message, LoggerNodeConfiguration.MaxMessageLength);

        var configured = c.Attributes ?? [];
        var attributes = new List<KeyValuePair<string, object?>>(configured.Count + 3);
        var etlContext = TryGetEtlContext(nodeContext);
        if (etlContext != null)
        {
            attributes.Add(new(TenantIdAttribute, etlContext.TenantId));
            attributes.Add(new(PipelineRtIdAttribute, etlContext.PipelineRtEntityId.RtId.ToString()));
        }

        attributes.Add(new(NodePathAttribute, nodeContext.NodePath.ToString()));

        foreach (var attribute in configured)
        {
            var value = !string.IsNullOrWhiteSpace(attribute.ValuePath)
                ? ReadValue(dataContext, nodeContext, attribute.ValuePath!, missingValue)
                : Redact(nodeContext, attribute.Value ?? missingValue);
            attributes.Add(new(attribute.Name, Limit(value, LoggerNodeConfiguration.MaxAttributeValueLength)));
        }

        nodeContext.Log(ToLogLevel(c.Level), message, attributes);

        await next(dataContext, nodeContext);
    }

    /// <summary>
    /// The ETL context of the execution, or <c>null</c> when it cannot be resolved as
    /// <see cref="IEtlContext" /> (the pipeline runs with a context type for which the host did not call
    /// <c>RegisterEtlContext</c>). A log line must never fail for that, so the tenant and pipeline
    /// attributes are then left out.
    /// </summary>
    private static IEtlContext? TryGetEtlContext(INodeContext nodeContext)
    {
        try
        {
            return nodeContext.ServiceProvider.GetService<IEtlContext>();
        }
        catch (DataPipelineException)
        {
            return null;
        }
    }

    private static void Validate(LoggerNodeConfiguration c, INodeContext nodeContext)
    {
        var hasMessage = c.Message != null;
        var hasMessagePath = !string.IsNullOrWhiteSpace(c.MessagePath);
        if (hasMessage == hasMessagePath)
        {
            throw new PipelineConfigurationException(
                $"[{nodeContext.NodePath}]: Logger@1 needs exactly one of 'message' and 'messagePath', " +
                (hasMessage ? "but both are set." : "but neither is set."));
        }

        if (!Enum.IsDefined(c.Level))
        {
            throw new PipelineConfigurationException(
                $"[{nodeContext.NodePath}]: Logger@1 'level' must be Debug, Information, Warning or Error.");
        }

        if (c.Attributes == null)
        {
            return;
        }

        if (c.Attributes.Count > LoggerNodeConfiguration.MaxAttributes)
        {
            throw new PipelineConfigurationException(
                $"[{nodeContext.NodePath}]: Logger@1 allows at most {LoggerNodeConfiguration.MaxAttributes} " +
                $"attributes, but {c.Attributes.Count} are configured.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < c.Attributes.Count; i++)
        {
            var attribute = c.Attributes[i];
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.Name))
            {
                throw new PipelineConfigurationException(
                    $"[{nodeContext.NodePath}]: Logger@1 attribute {i + 1} has no 'name'.");
            }

            // A name in braces ({OriginalFormat}) would make a provider treat the body as a template.
            if (ReservedAttributeNames.Contains(attribute.Name) || attribute.Name.StartsWith('{'))
            {
                throw new PipelineConfigurationException(
                    $"[{nodeContext.NodePath}]: Logger@1 attribute name '{attribute.Name}' is reserved.");
            }

            if (!names.Add(attribute.Name))
            {
                throw new PipelineConfigurationException(
                    $"[{nodeContext.NodePath}]: Logger@1 attribute name '{attribute.Name}' is used more than once.");
            }

            if (attribute.Value == null && string.IsNullOrWhiteSpace(attribute.ValuePath))
            {
                throw new PipelineConfigurationException(
                    $"[{nodeContext.NodePath}]: Logger@1 attribute '{attribute.Name}' needs 'value' or 'valuePath'.");
            }
        }
    }

    /// <summary>
    /// Reads the value at <paramref name="path" /> as redacted text: a string as it is, any other value
    /// as compact JSON, a missing or null value as <paramref name="missingValue" /> (already redacted).
    /// Objects and arrays are redacted as a tree before they are serialized (as in
    /// SetPipelineExecutionResult@1), property names included: JSON escaping would otherwise hide a
    /// secret that contains a quote or a backslash from the text redaction.
    /// </summary>
    private static string ReadValue(IDataContext dataContext, INodeContext nodeContext, string path,
        string missingValue)
    {
        switch (dataContext.GetKind(path))
        {
            case DataKind.Undefined:
            case DataKind.Null:
                return missingValue;
            case DataKind.String:
                return Redact(nodeContext, dataContext.Get<string>(path) ?? missingValue);
            default:
                var node = dataContext.Get<JsonNode>(path);
                if (node == null)
                {
                    return missingValue;
                }

                var registry = nodeContext.SecretRegistry;
                if (registry is { HasSecrets: true })
                {
                    node = registry.Redact(RedactPropertyNames(node.DeepClone(), registry)) ?? node;
                }

                return node.ToJsonString(SystemTextJsonOptions.Default);
        }
    }

    /// <summary>
    /// Masks registered secrets in the property names of <paramref name="node" /> (a clone owned by the
    /// caller); the tree redaction of <see cref="PipelineSecretRegistry" /> masks values only.
    /// </summary>
    private static JsonNode RedactPropertyNames(JsonNode node, PipelineSecretRegistry registry)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    if (child != null)
                    {
                        RedactPropertyNames(child, registry);
                    }

                    var redacted = registry.Redact(name) ?? name;
                    if (redacted != name)
                    {
                        obj.Remove(name);
                        // Two names masked to the same text keep the first value.
                        obj.TryAdd(redacted, child);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child != null)
                    {
                        RedactPropertyNames(child, registry);
                    }
                }

                break;
        }

        return node;
    }

    private static string Redact(INodeContext nodeContext, string value)
    {
        return nodeContext.SecretRegistry?.Redact(value) ?? value;
    }

    /// <summary>
    /// Cuts <paramref name="value" /> so that it ends with the truncation marker and is at most
    /// <paramref name="maxLength" /> characters long, without splitting a surrogate pair.
    /// </summary>
    private static string Limit(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var keep = maxLength - LoggerNodeConfiguration.TruncationMarker.Length;
        if (char.IsHighSurrogate(value[keep - 1]))
        {
            keep--;
        }

        return value.Substring(0, keep) + LoggerNodeConfiguration.TruncationMarker;
    }

    private static LogLevel ToLogLevel(LoggerNodeLevel level)
    {
        return level switch
        {
            LoggerNodeLevel.Debug => LogLevel.Debug,
            LoggerNodeLevel.Warning => LogLevel.Warning,
            LoggerNodeLevel.Error => LogLevel.Error,
            _ => LogLevel.Information
        };
    }
}
