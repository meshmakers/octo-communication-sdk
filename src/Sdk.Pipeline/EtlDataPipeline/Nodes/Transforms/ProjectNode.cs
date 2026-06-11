using System.Collections.Generic;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Project node configuration.
/// </summary>
[NodeName("Project", 1)]
public record ProjectNodeConfiguration : PathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the fields to project
    /// </summary>
    [PropertyGroup("Data Mapping", 0)]
    public required ICollection<FieldConfiguration> Fields { get; set; }

    /// <summary>
    /// If true, the properties of the root object will be cleared that is selected by 'Path'
    /// </summary>
    [PropertyGroup("Options", 1)]
    public bool Clear { get; set; } = false;
}

/// <summary>
/// Settings for a field to project
/// </summary>
public class FieldConfiguration
{
    /// <summary>
    /// JSON path to the source field
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// True if the field should be included, false if it should be removed
    /// </summary>
    public bool Inclusion { get; set; } = false;
}

/// <summary>
/// Projects an object to a new object
/// </summary>
[NodeConfiguration(typeof(ProjectNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class ProjectNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ProjectNodeConfiguration>();

        // For each match of c.Path, project the match's subtree and let UpdateMatchesAsync
        // write the projected node back to the match's canonical path. This restores legacy
        // parity for multi-match Path expressions (e.g. "$.items[*]") which previously used
        // Newtonsoft parent-pointer in-place mutation of every match. The prior STJ
        // implementation collapsed all matches to a single literal c.Path write (last-wins).
        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            var sourceNode = matchCtx.Get<JsonNode>("$");
            if (sourceNode is null)
            {
                return Task.CompletedTask;
            }

            // Snapshot of source for inclusion lookups (Clear+Inclusion mode reads from a pre-clear copy).
            var snapshot = sourceNode.DeepClone();

            JsonNode projected;
            if (c.Clear)
            {
                // Build a fresh empty object and copy in the included fields.
                var freshObject = new JsonObject();

                foreach (var fc in c.Fields)
                {
                    if (!fc.Inclusion)
                    {
                        continue;
                    }

                    var fieldValue = JsonPathWalker.Select(new NodeView(snapshot), fc.Path)
                        .Select(m => m.Match.Node)
                        .FirstOrDefault();
                    if (fieldValue is not null)
                    {
                        JsonNodePath.Set(freshObject, fc.Path, fieldValue);
                    }
                }

                projected = freshObject;
            }
            else
            {
                // Start from a clone of the source; remove fields configured as exclusions.
                var working = snapshot.DeepClone();

                foreach (var fc in c.Fields)
                {
                    if (fc.Inclusion)
                    {
                        continue;
                    }

                    if (!JsonNodePath.Remove(working, fc.Path))
                    {
                        // Field doesn't exist on working — only an error if the original
                        // (snapshot) had it. Use Any() (not FirstOrDefault) so an explicit-null
                        // value in the snapshot counts as "present" (matches Newtonsoft's
                        // JValue(Null) behavior — a first-match-or-null read would collapse it
                        // with "missing").
                        if (JsonPathWalker.Select(new NodeView(snapshot), fc.Path).Any())
                        {
                            nodeContext.Error($"Parent property not found for field {fc.Path}");
                            throw PipelineExecutionException.ParentPropertyNotFound(nodeContext.NodePath, fc.Path);
                        }
                    }
                }

                projected = working;
            }

            // Replace the sub-context root with the projected node. UpdateMatchesAsync
            // propagates this back to the match's canonical path in the parent context.
            matchCtx.Set("$", projected);
            return Task.CompletedTask;
        });

        await next(dataContext, nodeContext);
    }
}
