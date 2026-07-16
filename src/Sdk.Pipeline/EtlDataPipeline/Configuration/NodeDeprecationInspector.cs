using System.Collections.Concurrent;
using System.Reflection;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// A usage of a deprecated node configuration within a pipeline definition.
/// </summary>
/// <param name="QualifiedName">The qualified node name (e.g. "ApplyChanges@1")</param>
/// <param name="Message">Optional reason or migration hint from <see cref="NodeDeprecatedAttribute"/></param>
public record DeprecatedNodeUsage(string QualifiedName, string? Message);

/// <summary>
/// Detects usages of deprecated node configurations (see <see cref="NodeDeprecatedAttribute"/>)
/// in a pipeline definition. Attribute lookups are cached per configuration type.
/// </summary>
public static class NodeDeprecationInspector
{
    private static readonly ConcurrentDictionary<Type, DeprecatedNodeUsage?> Cache = new();

    /// <summary>
    /// Returns one entry per deprecated node type used in the definition (deduplicated by
    /// qualified name), including trigger nodes and nodes nested in child transformations.
    /// </summary>
    public static IReadOnlyList<DeprecatedNodeUsage> FindDeprecatedNodes(NodeDefinitionRoot nodeDefinitionRoot)
    {
        var result = new Dictionary<string, DeprecatedNodeUsage>();

        if (nodeDefinitionRoot.Triggers != null)
        {
            foreach (var trigger in nodeDefinitionRoot.Triggers)
            {
                Collect(trigger, result);
            }
        }

        CollectAll(nodeDefinitionRoot.Transformations, result);

        return result.Values.ToList();
    }

    private static void CollectAll(ICollection<NodeConfiguration>? transformations,
        Dictionary<string, DeprecatedNodeUsage> result)
    {
        if (transformations == null) return;

        foreach (var nodeConfiguration in transformations)
        {
            Collect(nodeConfiguration, result);

            if (nodeConfiguration is IChildNodeConfiguration childNodeConfiguration)
            {
                CollectAll(childNodeConfiguration.Transformations, result);
            }

            if (nodeConfiguration is SwitchNodeConfiguration switchNodeConfiguration)
            {
                foreach (var switchCase in switchNodeConfiguration.Cases)
                {
                    CollectAll(switchCase.Transformations, result);
                }

                CollectAll(switchNodeConfiguration.Default, result);
            }
        }
    }

    private static void Collect(INodeConfiguration nodeConfiguration, Dictionary<string, DeprecatedNodeUsage> result)
    {
        var usage = Cache.GetOrAdd(nodeConfiguration.GetType(), configType =>
        {
            var deprecatedAttr = configType.GetCustomAttribute<NodeDeprecatedAttribute>();
            if (deprecatedAttr == null) return null;

            var nodeNameAttr = configType.GetCustomAttribute<NodeNameAttribute>();
            var qualifiedName = nodeNameAttr != null
                ? $"{nodeNameAttr.Name}@{nodeNameAttr.Version}"
                : configType.Name;

            return new DeprecatedNodeUsage(qualifiedName, deprecatedAttr.Message);
        });

        if (usage != null)
        {
            result.TryAdd(usage.QualifiedName, usage);
        }
    }
}
