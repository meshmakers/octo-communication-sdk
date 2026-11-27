using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Opens the <see cref="RuntimeReadScope" /> node scope around a node (AB#9824). The node path and key leave out the
/// iteration segments (<c>[0]</c>, <c>[1]</c>, ...) that loop nodes add per item, so every iteration of a node is the
/// same node; the key is the node id (with the sequence numbers), so two nodes of the same type in one sequence stay
/// two nodes.
/// </summary>
internal static class RuntimeReadNodeScope
{
    /// <summary>
    /// Opens the node scope for <paramref name="nodeContext" />.
    /// </summary>
    /// <param name="nodeQualifiedName">The qualified name of the node, e.g. <c>GetRtEntitiesByType@1</c>.</param>
    /// <param name="nodeContext">The context of the node.</param>
    /// <returns>The scope; dispose it when the node returns.</returns>
    public static IDisposable Begin(string nodeQualifiedName, INodeContext nodeContext)
    {
        return RuntimeReadScope.BeginNode(nodeQualifiedName,
            WithoutIterationSegments(nodeContext.NodePath.ToString()),
            WithoutIterationSegments(nodeContext.NodeId.ToString()));
    }

    /// <summary>
    /// Removes the path segments a loop node adds per iteration (<c>[n]</c>, or <c>n:[n]</c> in a node id).
    /// </summary>
    internal static string WithoutIterationSegments(string path)
    {
        if (path.IndexOf('[') < 0)
        {
            return path;
        }

        var segments = path.Split('/');
        var kept = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (!IsIterationSegment(segment))
            {
                kept.Add(segment);
            }
        }

        return string.Join("/", kept);
    }

    private static bool IsIterationSegment(string segment)
    {
        // A node path segment "[3]", or a node id segment "3:[3]".
        var colon = segment.IndexOf(':');
        var name = colon >= 0 ? segment.Substring(colon + 1) : segment;
        if (name.Length < 3 || name[0] != '[' || name[name.Length - 1] != ']')
        {
            return false;
        }

        for (var i = 1; i < name.Length - 1; i++)
        {
            if (!char.IsDigit(name[i]))
            {
                return false;
            }
        }

        return true;
    }
}
