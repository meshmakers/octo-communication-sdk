using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Configuration for assign object node.
/// </summary>
[NodeName("SelectByPath", 1)]
public record SelectByPathNodeConfiguration : ObjectIteratorNodeConfiguration<PathPropertyConfigurationNode>;

/// <summary>
/// Contains transformation information of a property.
/// </summary>
public class PathPropertyConfigurationNode : TokenConfigurationNode
{
    /// <summary>
    /// Gets or sets the source path in json path format
    /// </summary>
    public string Path { get; set; } = $"$";

    /// <summary>
    /// Gets or sets the target path in json path format
    /// </summary>
    public string TargetPath { get; set; } = "$";

    /// <summary>
    /// Gets or sets the write mode (overwrite, append, prepend)
    /// </summary>
    public TargetValueWriteModes TargetValueTargetValueWriteMode { get; set; } = TargetValueWriteModes.Overwrite;

    /// <summary>
    /// Gets or sets the value kind to write (simple value or array)
    /// </summary>
    public ValueKinds TargetValueKind { get; set; } = ValueKinds.Simple;

    /// <summary>
    /// Gets or sets the document mode (extend, replace)
    /// </summary>
    public DocumentModes DocumentMode { get; set; } = DocumentModes.Extend;
}

/// <summary>
/// Transforms a list of properties from the source.
/// </summary>
[NodeConfiguration(typeof(SelectByPathNodeConfiguration))]
public class SelectByPathNode(NodeDelegate next) : ChildNodeBase
{
    private record UpdateItem
    {
        public required string TargetPath { get; init; }
        public required DocumentModes DocumentMode { get; init; }
        public required ValueKinds TargetValueKind { get; init; }
        public required TargetValueWriteModes TargetValueWriteMode { get; init; }
        public JsonNode? Value { get; init; }
    }

    /// <inheritdoc />
    public override async Task ProcessObjectAsync(IDataContext dataContext, INodeContext rootNodeContext)
    {
        var c = rootNodeContext.GetNodeConfiguration<SelectByPathNodeConfiguration>();
        if (dataContext.GetKind("$") == DataKind.Undefined)
        {
            await next(dataContext, rootNodeContext);
            return;
        }

        var updates = new ConcurrentBag<UpdateItem>();
        var tasks = new List<Task>();
        var factory = (IIterationContextFactory)dataContext;
        // No aliases for SelectByPath; resolve an empty list once so per-task children share
        // the same (empty) alias state.
        var aliases = factory.ResolveAliasElements(Array.Empty<(string, string)>());

        // Parallelize OVER the c.SelectPath collection — each selectPath is processed in its
        // own Task. Within a task we read the FIRST match of sel.Path only, matching legacy
        // SelectToken (singular) semantics. Multi-match Paths return the first match in
        // document order; downstream transformations and the outer write happen exactly once
        // per SelectPath entry.
        foreach (var sel in c.SelectPath)
        {
            var path = sel.Path;
            if (!dataContext.Exists(path))
            {
                rootNodeContext.Debug($"No token found for path: {path}. Skipping execution.");
                continue;
            }

            // First match only — Get<JsonNode> returns the first match of the JSONPath
            // expression (or null if no match). This restores legacy SelectToken parity:
            // multi-match expressions previously had every match's body output collide on
            // the same sel.TargetPath via last-write-wins, silently dropping all but the
            // final match. Reading just the first match makes the contract explicit.
            var firstMatch = dataContext.Get<JsonNode>(path);
            if (firstMatch is null)
            {
                rootNodeContext.Debug($"No token found for path: {path}. Skipping execution.");
                continue;
            }

            tasks.Add(Task.Run(async () =>
            {
                var pathCtx = factory.CreateIterationChild(aliases, firstMatch);
                var pathNodeContext = rootNodeContext.RegisterChildNode(0u, sel, pathCtx);
                var tokenNext = new NodeDelegate((ds, _) =>
                {
                    pathNodeContext.Unregister(ds);
                    var value = ds.Get<JsonNode>("$");
                    updates.Add(new UpdateItem
                    {
                        TargetPath = sel.TargetPath,
                        DocumentMode = sel.DocumentMode,
                        TargetValueKind = sel.TargetValueKind,
                        TargetValueWriteMode = sel.TargetValueTargetValueWriteMode,
                        Value = value?.DeepClone()
                    });
                    return Task.CompletedTask;
                });
                await ProcessChildTransformationsAsSequenceAsync(pathCtx, pathNodeContext, tokenNext, sel)
                    .ConfigureAwait(false);
            }));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var u in updates)
        {
            dataContext.Set(u.TargetPath, u.Value, u.DocumentMode, u.TargetValueKind, u.TargetValueWriteMode);
        }

        await next(dataContext, rootNodeContext);
    }
}
