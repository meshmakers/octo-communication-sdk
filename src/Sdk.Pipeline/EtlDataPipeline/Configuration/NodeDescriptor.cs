namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Describes a pipeline node type with its configuration schema.
/// </summary>
/// <param name="NodeName">The qualified name of the node (e.g. "Select@1")</param>
/// <param name="Version">The version of the node</param>
/// <param name="Category">The category derived from namespace (e.g. "Trigger", "Transform", "Control")</param>
/// <param name="IsTrigger">Whether this node is a trigger node</param>
/// <param name="SupportsChildren">Whether this node supports child transformations</param>
/// <param name="ConfigurationSchemaJson">JSON Schema string describing the configuration</param>
/// <param name="IsDeprecated">Whether this node is deprecated (see <see cref="NodeDeprecatedAttribute"/>)</param>
/// <param name="DeprecationMessage">Optional reason or migration hint when the node is deprecated</param>
/// <param name="RequiresRunningProcess">
///     Whether this trigger node only works while the adapter process is running
///     (see <see cref="NodeRequiresRunningProcessAttribute"/>)
/// </param>
public record NodeDescriptor(
    string NodeName,
    int Version,
    string Category,
    bool IsTrigger,
    bool SupportsChildren,
    string ConfigurationSchemaJson,
    bool IsDeprecated = false,
    string? DeprecationMessage = null,
    bool RequiresRunningProcess = false);
