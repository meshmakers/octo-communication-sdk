namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Marks a node configuration as deprecated. Deprecated nodes remain fully functional,
/// but the graphical editor flags them and the runtime emits warnings when a pipeline
/// using them is registered or executed. The flag is injected into the generated JSON
/// schema as the node-level extensions "x-deprecated" (true) and "x-deprecationMessage".
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NodeDeprecatedAttribute(string? message = null) : Attribute
{
    /// <summary>
    /// Optional reason or migration hint (e.g. "Use ApplyChanges@2 instead").
    /// </summary>
    public string? Message { get; } = message;
}
