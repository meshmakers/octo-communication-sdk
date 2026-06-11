using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Object iterator node configuration
/// </summary>
public abstract record ObjectIteratorNodeConfiguration<TSignalConfigurationNode> : NodeConfiguration
    where TSignalConfigurationNode : TokenConfigurationNode
{
    /// <summary>
    /// List of transformations to apply to the signal
    /// </summary>
    [PropertyGroup("Data Mapping", 0)]
    public required ICollection<TSignalConfigurationNode> SelectPath { get; set; } = null!;
}

/// <summary>
/// Transform configuration node for one token
/// </summary>
public class TokenConfigurationNode : IChildNodeConfiguration
{
    /// <inheritdoc />
    public string? Description { get; set; }

    /// <inheritdoc />
    public ICollection<NodeConfiguration>? Transformations { get; set; }
}

/// <summary>
/// Object iterator node
/// </summary>
public abstract class ObjectIteratorNode<TTokenConfigurationNode>
    : ChildNodeBase where TTokenConfigurationNode : TokenConfigurationNode
{
    /// <summary>
    /// Processes the iterator
    /// </summary>
    /// <param name="dataContext"></param>
    /// <param name="rootNodeContext"></param>
    /// <param name="nextDelegate"></param>
    /// <param name="iteratorConfigurationNode"></param>
    /// <exception cref="Exception"></exception>
    protected static async Task ProcessToken(IDataContext dataContext, INodeContext rootNodeContext,
        NodeDelegate nextDelegate, TTokenConfigurationNode iteratorConfigurationNode)
    {
        if (dataContext.GetKind("$") == DataKind.Array)
        {
            // Drive parallel iteration here so iteration bodies execute concurrently
            // instead of relying on the (sequential) IterateArrayAsync API.
            var sourceArray = dataContext.Get<JsonArray>("$");
            if (sourceArray is null || sourceArray.Count == 0)
            {
                dataContext.Set("$", new JsonArray());
                await nextDelegate(dataContext, rootNodeContext);
                return;
            }

            var factory = (IIterationContextFactory)dataContext;
            // No alias requirements for object iteration; resolve an empty list.
            var aliases = factory.ResolveAliasElements(Array.Empty<(string, string)>());

            var collected = new ConcurrentBag<JsonNode?>();
            var count = sourceArray.Count;

#if NETSTANDARD2_0
            var tasks = new List<Task>(count);
            for (var i = 0; i < count; i++)
            {
                var index = (uint)i;
                var item = sourceArray[i]?.DeepClone();
                tasks.Add(Task.Run(async () =>
                {
                    await RunIterationAsync(factory, aliases, item, index, rootNodeContext,
                        iteratorConfigurationNode, collected).ConfigureAwait(false);
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
#else
            await Parallel.ForAsync(0, count, async (i, _) =>
            {
                var index = (uint)i;
                var item = sourceArray[i]?.DeepClone();
                await RunIterationAsync(factory, aliases, item, index, rootNodeContext,
                    iteratorConfigurationNode, collected).ConfigureAwait(false);
            }).ConfigureAwait(false);
#endif

            var arr = new JsonArray();
            foreach (var item in collected) arr.Add(item?.DeepClone());
            dataContext.Set("$", arr);
            await nextDelegate(dataContext, rootNodeContext);
        }
        else
        {
            var singleNext = new NodeDelegate((ds, nc) =>
            {
                nc.Unregister(ds);
                var node = ds.Get<JsonNode>("$");
                dataContext.Set("$", node);
                return Task.CompletedTask;
            });
            await ProcessChildTransformationsAsSequenceAsync(dataContext, rootNodeContext, singleNext, iteratorConfigurationNode);
            await nextDelegate(dataContext, rootNodeContext);
        }
    }

    private static async Task RunIterationAsync(
        IIterationContextFactory factory,
        IReadOnlyList<(string AliasPath, System.Text.Json.JsonElement Value)> aliases,
        JsonNode? item,
        uint index,
        INodeContext rootNodeContext,
        TTokenConfigurationNode iteratorConfigurationNode,
        ConcurrentBag<JsonNode?> collected)
    {
        var itemCtx = factory.CreateIterationChild(aliases, item);
        var itemNodeContext = rootNodeContext.RegisterChildNode(index, iteratorConfigurationNode, itemCtx);
        var arrayNext = new NodeDelegate((ds, _) =>
        {
            itemNodeContext.Unregister(ds);
            var node = ds.Get<JsonNode>("$");
            if (node is not null) collected.Add(node.DeepClone());
            return Task.CompletedTask;
        });
        await ProcessChildTransformationsAsSequenceAsync(itemCtx, itemNodeContext, arrayNext,
            iteratorConfigurationNode).ConfigureAwait(false);
    }
}
