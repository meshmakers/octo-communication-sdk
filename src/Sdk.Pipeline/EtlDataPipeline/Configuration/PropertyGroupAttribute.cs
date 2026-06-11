namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Specifies the display group, ordering, and input type hint for a node configuration property.
/// These values are injected as JSON Schema extension properties (x-group, x-order, x-input)
/// and consumed by the frontend property editor for layout and input component selection.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PropertyGroupAttribute : Attribute
{
    /// <summary>
    /// Creates a new PropertyGroupAttribute.
    /// </summary>
    /// <param name="group">Display group name (e.g., "General", "Paths", "Entity")</param>
    /// <param name="order">Display order within the group (lower = first)</param>
    /// <param name="inputHint">Optional input type hint (e.g., "jsonpath", "textarea", "ckTypeSelector")</param>
    public PropertyGroupAttribute(string group, int order = 0, string? inputHint = null)
    {
        Group = group;
        Order = order;
        InputHint = inputHint;
    }

    /// <summary>
    /// The display group name for this property.
    /// </summary>
    public string Group { get; }

    /// <summary>
    /// Display order within the group (lower values appear first).
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Optional input type hint for the frontend form builder.
    /// Supported values: "text", "textarea", "jsonpath", "ckTypeSelector", "password", "code"
    /// </summary>
    public string? InputHint { get; }
}
