using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Configuration for a for loop node.
/// </summary>
[NodeName("ForEach", 1)]
public record ForEachNodeConfiguration : SourceTargetPathNodeConfiguration, IChildNodeConfiguration
{
    /// <inheritdoc />
    public required ICollection<NodeConfiguration>? Transformations { get; set; }

    /// <summary>
    /// Gets or sets the path to the full document.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string FullDocumentPath { get; set; } = "$.full";

    /// <summary>
    /// Gets or sets the path to the array to iterate over.
    /// </summary>
    [PropertyGroup("Paths", 0, "jsonpath")]
    public required string IterationPath { get; set; }

    /// <summary>
    /// Gets or sets the path to the key of the current iteration.
    /// </summary>
    [PropertyGroup("Paths", 3, "jsonpath")]
    public string KeyPath { get; set; } = "$.key";

    /// <summary>
    /// Gets or sets the path used to merge the results of the child nodes.
    /// </summary>
    [PropertyGroup("Paths", 4, "jsonpath")]
    public string MergePath { get; set; } = "$.key";

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for the loop.
    /// 0 = use Environment.ProcessorCount (default), -1 = unlimited, positive = explicit limit.
    /// </summary>
    [PropertyGroup("Execution", 10)]
    public int MaxDegreeOfParallelism { get; set; }
}

/// <summary>
/// Continuously processes the child nodes for each element in an array.
/// </summary>
/// <param name="next">The next node in the pipeline</param>
[NodeConfiguration(typeof(ForEachNodeConfiguration))]
public class ForEachNode(NodeDelegate next) : ChildNodeBase
{
    /// <inheritdoc />
    public override async Task ProcessObjectAsync(IDataContext dataContext, INodeContext rootNodeContext)
    {
        var c = rootNodeContext.GetNodeConfiguration<ForEachNodeConfiguration>();

        if (!dataContext.Exists(c.Path))
        {
            throw PipelineExecutionException.PathNotFound(rootNodeContext.NodePath, c.Path);
        }
        if (dataContext.GetKind(c.IterationPath) != DataKind.Array)
        {
            throw PipelineExecutionException.PathMustBeArray(rootNodeContext.NodePath, nameof(c.IterationPath), c.IterationPath);
        }

        // Bypass IDataContext.IterateArrayAsync (sequential by design) and drive iteration
        // here so we can run iteration bodies in parallel honoring c.MaxDegreeOfParallelism.
        // The framework's IterateArrayAsync stays sequential for callers that don't need
        // parallelism; iteration *nodes* manage their own.
        var sourceArray = dataContext.Get<JsonArray>(c.IterationPath);
        if (sourceArray is null || sourceArray.Count == 0)
        {
            // No items to iterate. Still produce an empty result array and continue.
            dataContext.Set(c.TargetPath, new JsonArray(), c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
            await next(dataContext, rootNodeContext);
            return;
        }

        // Resolve the FullDocumentPath alias once up-front (zero-copy across iterations).
        var factory = (IIterationContextFactory)dataContext;
        var aliases = factory.ResolveAliasElements(new[] { (c.FullDocumentPath, c.Path) });

        var collected = new ConcurrentBag<JsonNode?>();

        var maxDop = c.MaxDegreeOfParallelism switch
        {
            0 => Environment.ProcessorCount,
            -1 => -1,
            _ => c.MaxDegreeOfParallelism
        };
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDop };

        var count = sourceArray.Count;

#if NETSTANDARD2_0
        // netstandard2.0 has no Parallel.ForAsync; fall back to Task.Run + WhenAll with a
        // SemaphoreSlim to honor maxDop. Unlimited (-1) means no semaphore gating.
        SemaphoreSlim? gate = maxDop > 0 ? new SemaphoreSlim(maxDop, maxDop) : null;
        try
        {
            var tasks = new List<Task>(count);
            for (var i = 0; i < count; i++)
            {
                var index = (uint)i;
                var item = sourceArray[i]?.DeepClone();
                tasks.Add(Task.Run(async () =>
                {
                    if (gate is not null) await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await RunIterationAsync(factory, aliases, item, index, c, rootNodeContext, collected)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        if (gate is not null) gate.Release();
                    }
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            gate?.Dispose();
        }
#else
        await Parallel.ForAsync(0, count, parallelOptions, async (i, _) =>
        {
            var index = (uint)i;
            var item = sourceArray[i]?.DeepClone();
            await RunIterationAsync(factory, aliases, item, index, c, rootNodeContext, collected)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
#endif

        var resultArray = new JsonArray();
        foreach (var item in collected) resultArray.Add(item?.DeepClone());
        dataContext.Set(c.TargetPath, resultArray, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, rootNodeContext);
    }

    private static async Task RunIterationAsync(
        IIterationContextFactory factory,
        IReadOnlyList<(string AliasPath, JsonElement Value)> aliases,
        JsonNode? item,
        uint index,
        ForEachNodeConfiguration c,
        INodeContext rootNodeContext,
        ConcurrentBag<JsonNode?> collected)
    {
        // Seed the iteration item at the configured KeyPath (default "$.key") rather
        // than at the child's root. Many pipelines and the default MergePath ($.key)
        // depend on the item being available under KeyPath. Pass null to the factory
        // so the child's root stays untouched (so parent fallback continues to work),
        // then explicitly write the item to KeyPath.
        var itemCtx = factory.CreateIterationChild(aliases, null);
        if (item is not null)
        {
            itemCtx.Set(c.KeyPath, item);
        }
        var itemNodeContext = rootNodeContext.RegisterChildNode(index, c, itemCtx);
        var arrayNext = new NodeDelegate((ds, _) =>
        {
            itemNodeContext.Unregister(ds);
            var mergeItem = ds.Get<JsonNode>(c.MergePath);
            if (mergeItem is not null) collected.Add(mergeItem.DeepClone());
            return Task.CompletedTask;
        });

        await ProcessChildTransformationsAsSequenceAsync(itemCtx, itemNodeContext, arrayNext, c)
            .ConfigureAwait(false);
    }
}
