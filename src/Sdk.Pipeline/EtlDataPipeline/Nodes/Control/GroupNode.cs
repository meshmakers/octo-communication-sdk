using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// A purely structural, no-op container used to group nodes in the graphical editor
/// (like a named subroutine/region). It has NO effect on the pipeline data or execution —
/// running nodes inside a Group is identical to listing them inline. Its only purpose is to
/// give a set of nodes a name and let the editor collapse/hide them.
/// </summary>
[NodeName("Group", 1)]
[NodeKind("group")]
public record GroupNodeConfiguration : NodeConfiguration, IChildNodeConfiguration
{
    /// <summary>
    /// The grouped child nodes. Executed in order on the same data context.
    /// </summary>
    public required ICollection<NodeConfiguration>? Transformations { get; set; }

    /// <summary>
    /// Display name of the group, shown as its label in the editor.
    /// </summary>
    [PropertyGroup("General", 1)]
    public string? Name { get; set; }
}

/// <summary>
/// Executes its grouped child transformations in order on the same data context, then continues
/// to the next node. Behaviorally a no-op container: identical to listing the children inline.
/// </summary>
[NodeConfiguration(typeof(GroupNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class GroupNode(NodeDelegate next) : ChildNodeBase
{
    /// <inheritdoc />
    public override async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<GroupNodeConfiguration>();

        // Run the grouped children on the SAME data context — exactly as SwitchNode does for a
        // matched body. Writes mutate the live context directly; nothing is re-cloned and the data
        // context is NEVER reallocated. The children see the live "$" (including any enclosing
        // ForEach "$.full" aliases), so grouping is behaviourally identical to inlining.
        await ProcessChildTransformationsAsSequenceAsync(dataContext, nodeContext,
            static (_, _) => Task.CompletedTask, c);

        await next(dataContext, nodeContext);
    }
}
