using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
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

    /// <summary>
    /// Gets or sets whether the loop continues when an iteration fails.
    /// Off (default), the first iteration error aborts the loop and propagates unchanged.
    /// On, iteration errors are logged and collected, the remaining iterations and the
    /// downstream nodes still run, and the node afterwards fails the execution with an
    /// aggregated error — a single poisoned element cannot starve the remaining ones,
    /// while the run still reports every failure. Cancellation always aborts the loop.
    /// </summary>
    [PropertyGroup("Execution", 11)]
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the path the collected iteration errors are written to. Requires
    /// <c>continueOnError</c>. Must not equal or overlap <c>targetPath</c>: the errors are
    /// written after the result, so an overlapping path would silently replace or corrupt
    /// the result - that collision is rejected before the first iteration.
    /// Unset (default), iteration errors are only logged and reported afterwards as the
    /// aggregated execution error.
    /// Set, the collected errors are written to this path as an array of
    /// <c>{ index, errorType, message }</c> objects - empty when nothing failed - before the
    /// downstream nodes run, and the node no longer fails the execution afterwards: the pipeline
    /// owns the errors and decides itself whether the run turns red, for example by routing the
    /// failed items to a quarantine or by a later node that throws.
    /// </summary>
    [PropertyGroup("Paths", 5, "jsonpath")]
    public string? ErrorsPath { get; set; }
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

        // Configuration guards run before any data work: errorsPath only has meaning while the
        // loop keeps collecting failures instead of aborting on the first one.
        if (c.ErrorsPath is not null)
        {
            if (string.IsNullOrWhiteSpace(c.ErrorsPath))
            {
                throw PipelineExecutionException.ConfigurationPropertyEmpty(rootNodeContext.NodePath,
                    nameof(c.ErrorsPath));
            }
            if (!c.ContinueOnError)
            {
                throw PipelineExecutionException.ConfigurationPropertyRequires(rootNodeContext.NodePath,
                    nameof(c.ErrorsPath), nameof(c.ContinueOnError));
            }
            if (string.Equals(c.ErrorsPath, c.TargetPath, StringComparison.Ordinal))
            {
                // The errors are written after the result, so the same path would silently
                // replace the result array instead of failing.
                throw PipelineExecutionException.ConfigurationPropertiesMustDiffer(rootNodeContext.NodePath,
                    nameof(c.ErrorsPath), nameof(c.TargetPath), c.ErrorsPath);
            }
            // DataContext.Set treats a null/empty path as the document root, so the overlap
            // test must see the effective target "$" in that case.
            var targetPath = string.IsNullOrEmpty(c.TargetPath) ? "$" : c.TargetPath;
            if (CanonicalPath.IsAncestor(c.ErrorsPath, targetPath) ||
                CanonicalPath.IsAncestor(targetPath, c.ErrorsPath))
            {
                // Strict containment is as fatal as the equality above: an errorsPath enclosing
                // targetPath replaces the subtree the result was just written into (with "$" the
                // whole document, silently), one inside targetPath corrupts the result array,
                // and with the root as target the errors write cannot even succeed - the result
                // write has already turned the document into an array.
                throw PipelineExecutionException.ConfigurationPropertyPathsMustNotOverlap(
                    rootNodeContext.NodePath, nameof(c.ErrorsPath), nameof(c.TargetPath),
                    c.ErrorsPath, c.TargetPath);
            }
        }

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
            if (c.ErrorsPath is not null)
            {
                dataContext.Set(c.ErrorsPath, new JsonArray());
            }
            await next(dataContext, rootNodeContext);
            return;
        }

        // Resolve the FullDocumentPath alias once up-front (zero-copy across iterations).
        var factory = (IIterationContextFactory)dataContext;
        var aliases = factory.ResolveAliasElements(new[] { (c.FullDocumentPath, c.Path) });

        // Collected WITH the iteration index: ConcurrentBag enumeration is unordered
        // (LIFO per thread), so materializing it directly scrambled the result array
        // relative to the source array — fatal for order-dependent consumers such as
        // page merges (AB#4760). Null merge results are skipped, matching the old
        // behavior, so the result is the ordered sequence of non-null items.
        var collected = new ConcurrentBag<(uint Index, JsonNode Item)>();

        // With ContinueOnError, per-iteration failures land here instead of aborting the
        // loop; they are reported as one aggregated failure after the loop and next() ran.
        var iterationErrors = c.ContinueOnError ? new ConcurrentQueue<(uint Index, Exception Error)>() : null;

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
        var abortRequested = 0;
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
                        // Match Parallel.ForAsync semantics: after a failing iteration (only
                        // possible without ContinueOnError - with it, failures are collected)
                        // no further iterations are started; in-flight ones complete.
                        if (Volatile.Read(ref abortRequested) != 0)
                        {
                            return;
                        }

                        try
                        {
                            await RunIterationAsync(factory, aliases, item, index, c, rootNodeContext, collected,
                                    iterationErrors)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            Interlocked.Exchange(ref abortRequested, 1);
                            throw;
                        }
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
            await RunIterationAsync(factory, aliases, item, index, c, rootNodeContext, collected, iterationErrors)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
#endif

        // The bag items are exclusively owned (see arrayNext above) and parentless, so they can be
        // attached to the result array directly. Cloning here doubled the peak memory of every
        // ForEach result for the rest of the run (AB#4662). Ordered by iteration index so the
        // result array matches the source array order regardless of scheduling.
        var resultArray = new JsonArray();
        foreach (var (_, item) in collected.OrderBy(x => x.Index)) resultArray.Add(item);
        dataContext.Set(c.TargetPath, resultArray, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        // After the result write, whose DocumentMode may reset the document root, and before the
        // downstream nodes - they are the ones that act on the failures.
        if (c.ErrorsPath is not null)
        {
            dataContext.Set(c.ErrorsPath, BuildIterationErrors(iterationErrors));
        }

        await next(dataContext, rootNodeContext);

        // With ErrorsPath the pipeline owns the errors and decides itself whether the run turns
        // red; without it the collected failures are reported as the aggregated execution error.
        if (iterationErrors is { IsEmpty: false } && c.ErrorsPath is null)
        {
            throw PipelineExecutionException.IterationsFailed(rootNodeContext.NodePath, count,
                iterationErrors.OrderBy(x => x.Index).ToArray());
        }
    }

    // Iteration failures as pipeline data: the same (index, exception) pairs the aggregated error
    // reports, in ascending index order so the array lines up with the source array. Child
    // exception messages belong here - a pipeline routing failed items needs them - unlike in the
    // aggregated error text, which stays free of payload content.
    private static JsonArray BuildIterationErrors(ConcurrentQueue<(uint Index, Exception Error)>? iterationErrors)
    {
        var errors = new JsonArray();
        if (iterationErrors is null)
        {
            return errors;
        }

        foreach (var (index, error) in iterationErrors.OrderBy(x => x.Index))
        {
            errors.Add(new JsonObject
            {
                ["index"] = index,
                ["errorType"] = error.GetType().Name,
                ["message"] = error.Message
            });
        }

        return errors;
    }

    private static async Task RunIterationAsync(
        IIterationContextFactory factory,
        IReadOnlyList<(string AliasPath, JsonElement Value)> aliases,
        JsonNode? item,
        uint index,
        ForEachNodeConfiguration c,
        INodeContext rootNodeContext,
        ConcurrentBag<(uint Index, JsonNode Item)> collected,
        ConcurrentQueue<(uint Index, Exception Error)>? iterationErrors)
    {
        INodeContext? itemNodeContext = null;
        IDataContext? itemCtx = null;
        try
        {
            await RunIterationCoreAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (iterationErrors is not null && e is not OperationCanceledException)
        {
            // Cancellation still aborts the loop; any other iteration failure is isolated
            // so one poisoned element cannot starve the remaining ones.
            iterationErrors.Enqueue((index, e));
            try
            {
                // Log on the iteration's own context (its node id carries the iteration
                // index) and close its debug point so debug mode shows the failed
                // iteration's final state instead of a dangling point. No format args:
                // diagnostics must never throw out of this error path.
                itemNodeContext?.Error(e, "Iteration failed, continuing with the remaining iterations");
                if (itemCtx is not null)
                {
                    itemNodeContext?.Unregister(itemCtx);
                }
            }
            catch
            {
                // A failing logger or debugger must not abort the remaining iterations.
            }
        }

        return;

        async Task RunIterationCoreAsync()
        {
            // Seed the iteration item at the configured KeyPath (default "$.key") rather
            // than at the child's root. Many pipelines and the default MergePath ($.key)
            // depend on the item being available under KeyPath. Pass null to the factory
            // so the child's root stays untouched (so parent fallback continues to work),
            // then explicitly write the item to KeyPath.
            var ctx = factory.CreateIterationChild(aliases, null);
            itemCtx = ctx;
            if (item is not null)
            {
                ctx.Set(c.KeyPath, item);
            }
            var iterationNodeContext = rootNodeContext.RegisterChildNode(index, c, ctx);
            itemNodeContext = iterationNodeContext;
            var arrayNext = new NodeDelegate((ds, _) =>
            {
                iterationNodeContext.Unregister(ds);
                // Get<JsonNode> already returns an exclusively-owned clone — no further copy needed.
                var mergeItem = ds.Get<JsonNode>(c.MergePath);
                if (mergeItem is not null) collected.Add((index, mergeItem));
                return Task.CompletedTask;
            });

            await ProcessChildTransformationsAsSequenceAsync(ctx, iterationNodeContext, arrayNext, c)
                .ConfigureAwait(false);
        }
    }
}
