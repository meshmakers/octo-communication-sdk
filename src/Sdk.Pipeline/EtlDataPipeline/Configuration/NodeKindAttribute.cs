namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Marks a node configuration with a machine-readable "kind" for the graphical editor.
/// The value is injected into the generated JSON schema as the node-level extension
/// "x-nodeKind". For example, "group" tells the editor to render the node as a collapsible
/// region rather than a data-processing node.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NodeKindAttribute(string kind) : Attribute
{
    /// <summary>
    /// The node kind (e.g. "group").
    /// </summary>
    public string Kind { get; } = kind;
}
