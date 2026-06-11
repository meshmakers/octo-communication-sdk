using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Child node configuration
/// </summary>
public interface IChildNodeConfiguration : INodeConfiguration
{
    /// <summary>
    /// Child transformations of the current node
    /// </summary>
    ICollection<NodeConfiguration>? Transformations { get; set; }
}

/// <summary>
/// A node with child transformations
/// </summary>
public abstract class ChildNodeBase : IPipelineNode
{
    /// <summary>
    /// Processes the child transformations
    /// </summary>
    /// <param name="dataContext">Context to access the current pipeline data.</param>
    /// <param name="rootNodeContext">Context to access the current node data.</param>
    /// <param name="next">Next delegate when the current transformation is completed</param>
    /// <param name="c">Configuration of the child node containing a transformation list</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    protected static async Task ProcessChildTransformationsAsSequenceAsync(IDataContext dataContext, INodeContext rootNodeContext, NodeDelegate next,
        IChildNodeConfiguration c)
    {
        if (c.Transformations == null)
        {
            await next(dataContext, rootNodeContext);
            return;
        }

        var nodeLookupService = rootNodeContext.ServiceProvider.GetRequiredService<INodeLookupService>();

        // This is the last delegate in the sequence -> it will call the next node in the pipeline
        var nextDelegate = new NodeDelegate(async (ds, nc) =>
        {
            nc.Unregister(ds);
            await next(ds, nc);
        });

        uint sequenceNumber = 0;
        foreach (var nodeConfiguration in c.Transformations.Reverse())
        {
            if (!nodeLookupService.TryGetNodeConfigurationQualifiedName(nodeConfiguration.GetType(),
                    out var nodeQualifiedName))
            {
                throw DataPipelineException.UnknownConfigurationType(nodeConfiguration.GetType());
            }

            if (!nodeLookupService.TryCreateInstance(rootNodeContext.ServiceProvider, nodeQualifiedName!, nextDelegate,
                    out var node))
            {
                throw DataPipelineException.UnknownObjectPipelineNode(nodeQualifiedName!);
            }

            // This is the next delegate in the sequence -> it will call the next node in the sequence
            nextDelegate = async (dc, nc) =>
            {
                // We need to complete the parent node context, so For[0] is completed after the last child node
                if (nc != rootNodeContext)
                {
                    nc.Unregister(dc);
                }

                var childNodeContext = rootNodeContext.RegisterChildNode(nodeQualifiedName!, sequenceNumber++,
                    nodeConfiguration, dc);
                childNodeContext.Debug("Forward Executing (child)");
                await node!.ProcessObjectAsync(dc, childNodeContext);
                childNodeContext.Debug("Reverse completed (child)");
            };
        }

        await nextDelegate(dataContext, rootNodeContext);
    }

    /// <inheritdoc />
    public abstract Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext);
}