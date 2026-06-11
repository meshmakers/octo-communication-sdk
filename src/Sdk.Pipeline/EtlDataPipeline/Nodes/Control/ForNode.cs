using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Configuration for a for loop node.
/// </summary>
[NodeName("For", 1)]
public record ForNodeConfiguration : SourceTargetPathNodeConfiguration, IChildNodeConfiguration
{
    /// <inheritdoc />
    public ForNodeConfiguration()
    {
        TargetValueKind = ValueKinds.Simple;
    }

    /// <summary>
    /// The number of iterations (static value). Used when <see cref="CountPath"/> is not set.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public uint Count { get; set; }

    /// <summary>
    /// JSON path to dynamically resolve the iteration count from the data context.
    /// Takes precedence over <see cref="Count"/> when set.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public string? CountPath { get; set; }

    /// <summary>
    /// Path the index of the current iteration is stored.
    /// </summary>
    [PropertyGroup("Paths", 3, "jsonpath")]
    public string? IndexTargetPath { get; set; }

    /// <inheritdoc />
    public required ICollection<NodeConfiguration>? Transformations { get; set; }

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for the loop.
    /// 0 = use Environment.ProcessorCount (default), -1 = unlimited, positive = explicit limit.
    /// </summary>
    [PropertyGroup("Execution", 10)]
    public int MaxDegreeOfParallelism { get; set; }
}

/// <summary>
/// Continuously processes the child nodes for a specified number of iterations.
/// </summary>
/// <param name="next">The next node in the pipeline</param>
[NodeConfiguration(typeof(ForNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class ForNode(NodeDelegate next) : ChildNodeBase
{
    /// <inheritdoc />
    public override async Task ProcessObjectAsync(IDataContext dataContext, INodeContext rootNodeContext)
    {
        var c = rootNodeContext.GetNodeConfiguration<ForNodeConfiguration>();
        var count = ResolveCount(dataContext, c);
        // Resolve the loop's input subtree ONCE as an owned, alias-folded JsonElement and share it
        // (immutable, read-only) across all iterations. Folding carries an enclosing ForEach's
        // "$.full" aliases into the element, so "$.full[.full]" reads inside the loop body resolve
        // — the former per-iteration Select("$") + CreateSubContext snapshotted the overlay-only
        // "$" (dropping those aliases) and re-serialized/re-parsed the document on every iteration.
        // JsonElement reads are thread-safe, so one element backs every concurrent child.
        var inputElement = ((IIterationContextFactory)dataContext)
            .ResolveAliasElements(new[] { ("$", c.Path) })[0].Value;
        var targetArray = new ConcurrentBag<JsonNode?>();

        var maxDop = c.MaxDegreeOfParallelism switch
        {
            0 => Environment.ProcessorCount,
            -1 => -1,
            _ => c.MaxDegreeOfParallelism
        };

#if NETSTANDARD2_0
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDop };
        // ReSharper disable once AsyncVoidLambda
        Parallel.For(0, (int)count, parallelOptions, async (i, _) =>
        {
            var index = (uint)i;
#else
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDop };
        await Parallel.ForAsync<uint>(0, count, parallelOptions, async (index, _) =>
        {
#endif
            // Each iteration gets its own context over the SHARED input element: reads are zero-copy
            // off the immutable element, writes are isolated to this iteration's own overlay. The
            // context owns no JsonDocument (the element's backing is rooted by inputElement for the
            // loop's lifetime), so there is nothing to dispose per iteration.
            var itemDataContext = new DataContextImpl(inputElement);
            var itemNodeContext = rootNodeContext.RegisterChildNode(index, c, itemDataContext);

            var arrayNext = new NodeDelegate((dc, nc) =>
            {
                itemNodeContext.Unregister(dc);
                var produced = dc.Get<JsonNode>("$");
                if (produced is not null)
                {
                    targetArray.Add(produced.DeepClone());
                }

                return Task.CompletedTask;
            });

            if (!string.IsNullOrWhiteSpace(c.IndexTargetPath))
            {
                itemDataContext.Set(c.IndexTargetPath!, index, DocumentModes.Extend, ValueKinds.Simple,
                    TargetValueWriteModes.Overwrite);
            }

            await ProcessChildTransformationsAsSequenceAsync(itemDataContext, itemNodeContext, arrayNext, c);
        });

        var resultArray = new JsonArray();
        foreach (var item in targetArray)
        {
            resultArray.Add(item?.DeepClone());
        }
        dataContext.Set(c.TargetPath, resultArray, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, rootNodeContext);
    }

    private static uint ResolveCount(IDataContext dataContext, ForNodeConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.CountPath))
        {
            var resolved = dataContext.Get<long?>(config.CountPath!);
            if (resolved == null)
            {
                throw new InvalidOperationException(
                    $"CountPath '{config.CountPath}' resolved to null. Provide a valid integer value at the specified path.");
            }

            if (resolved < 0)
            {
                throw new InvalidOperationException(
                    $"CountPath '{config.CountPath}' resolved to {resolved}. Count must be a non-negative integer.");
            }

            return (uint)resolved.Value;
        }

        return config.Count;
    }
}
