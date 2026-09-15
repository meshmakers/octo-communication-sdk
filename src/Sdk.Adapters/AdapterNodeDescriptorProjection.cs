using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Projects the process's registered pipeline nodes onto the wire shape the controller stores
///     (AB#4924).
/// </summary>
/// <remarks>
///     🔴 <b>One source, two registration paths.</b> A dedicated adapter reports its descriptors on
///     <c>RegisterAdapterWithSchemaAsync</c>; a pool member reports the same ones on
///     <c>RegisterPoolMemberAsync</c>, because the controller has to answer the same questions
///     ("what can this process run", "is this trigger process-bound", "what class is this pipeline")
///     for a borrowed process as for an owned one. Building the projection twice is how the two
///     answers would start to differ.
/// </remarks>
public static class AdapterNodeDescriptorProjection
{
    /// <summary>
    ///     Projects every registered node, or null when there is no registry or the scan throws.
    ///     Never propagates: a process that cannot describe its nodes must still be able to
    ///     register — it degrades to the controller's name-based fallback.
    /// </summary>
    public static IReadOnlyList<NodeDescriptorDto>? TryProject(INodeSchemaRegistry? nodeSchemaRegistry,
        Action<Exception>? onError = null)
    {
        if (nodeSchemaRegistry == null)
        {
            return null;
        }

        try
        {
            return nodeSchemaRegistry.GetAllDescriptors().Select(d => new NodeDescriptorDto(
                d.NodeName,
                d.Version,
                d.Category,
                d.IsTrigger,
                d.SupportsChildren,
                d.ConfigurationSchemaJson,
                d.IsDeprecated,
                d.DeprecationMessage,
                d.RequiresRunningProcess,
                (int)d.ExecutionClass)).ToList();
        }
        catch (Exception e)
        {
            onError?.Invoke(e);
            return null;
        }
    }

    /// <summary>
    ///     Generates the composite pipeline JSON Schema, or null when there is no generator or the
    ///     generation throws. Same never-propagates contract as <see cref="TryProject" />.
    /// </summary>
    public static string? TryGenerateSchema(IPipelineSchemaGenerator? pipelineSchemaGenerator,
        Action<Exception>? onError = null)
    {
        if (pipelineSchemaGenerator == null)
        {
            return null;
        }

        try
        {
            return pipelineSchemaGenerator.GenerateSchema();
        }
        catch (Exception e)
        {
            onError?.Invoke(e);
            return null;
        }
    }
}
