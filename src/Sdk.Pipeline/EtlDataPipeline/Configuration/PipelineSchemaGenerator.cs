using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

internal class PipelineSchemaGenerator : IPipelineSchemaGenerator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false
    };

    private readonly INodeSchemaRegistry _registry;
    private string? _cachedSchema;
    private readonly object _lock = new();

    public PipelineSchemaGenerator(INodeSchemaRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public string GenerateSchema()
    {
        if (_cachedSchema != null) return _cachedSchema;

        lock (_lock)
        {
            if (_cachedSchema != null) return _cachedSchema;
            _cachedSchema = BuildSchema();
            return _cachedSchema;
        }
    }

    private string BuildSchema()
    {
        var descriptors = _registry.GetAllDescriptors();
        var triggers = new List<NodeDescriptor>();
        var transformations = new List<NodeDescriptor>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.IsTrigger)
                triggers.Add(descriptor);
            else
                transformations.Add(descriptor);
        }

        var rootDefs = new JsonObject();

        var triggerOneOf = new JsonArray();
        foreach (var trigger in triggers)
        {
            var nodeSchema = BuildNodeSchema(trigger, rootDefs, isTransformation: false);
            triggerOneOf.Add(nodeSchema);
        }

        var transformationOneOf = new JsonArray();
        foreach (var transformation in transformations)
        {
            var nodeSchema = BuildNodeSchema(transformation, rootDefs, isTransformation: true);
            transformationOneOf.Add(nodeSchema);
        }

        // Build the TriggerNode and TransformationNode $defs
        if (triggerOneOf.Count > 0)
        {
            rootDefs["TriggerNode"] = new JsonObject { ["oneOf"] = triggerOneOf };
        }
        else
        {
            rootDefs["TriggerNode"] = new JsonObject { ["type"] = "object" };
        }

        if (transformationOneOf.Count > 0)
        {
            rootDefs["TransformationNode"] = new JsonObject { ["oneOf"] = transformationOneOf };
        }
        else
        {
            rootDefs["TransformationNode"] = new JsonObject { ["type"] = "object" };
        }

        var rootSchema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["triggers"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["$ref"] = "#/$defs/TriggerNode" }
                },
                ["transformations"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["$ref"] = "#/$defs/TransformationNode" }
                }
            },
            ["$defs"] = rootDefs
        };

        return rootSchema.ToJsonString(WriteOptions);
    }

    private static JsonObject BuildNodeSchema(NodeDescriptor descriptor, JsonObject rootDefs, bool isTransformation)
    {
        var qualifiedName = $"{descriptor.NodeName}@{descriptor.Version}";

        // Parse the configuration schema
        JsonObject nodeSchema;
        try
        {
            var parsed = JsonNode.Parse(descriptor.ConfigurationSchemaJson);
            nodeSchema = parsed as JsonObject ?? new JsonObject { ["type"] = "object" };
        }
        catch
        {
            nodeSchema = new JsonObject { ["type"] = "object" };
        }

        // Ensure the schema has a type and properties
        if (nodeSchema["type"] == null)
        {
            nodeSchema["type"] = "object";
        }

        var properties = nodeSchema["properties"] as JsonObject;
        if (properties == null)
        {
            properties = new JsonObject();
            nodeSchema["properties"] = properties;
        }

        // Inject the discriminator "type" property with a const value
        properties["type"] = new JsonObject
        {
            ["type"] = "string",
            ["const"] = qualifiedName
        };

        // Ensure "type" is in the required array
        var required = nodeSchema["required"] as JsonArray ?? new JsonArray();
        var hasType = false;
        foreach (var r in required)
        {
            if (r.AsString() == "type")
            {
                hasType = true;
                break;
            }
        }
        if (!hasType)
        {
            required.Add("type");
        }
        // Detach before re-assignment to avoid 'parent already set' exception
        if (nodeSchema["required"] != null)
        {
            nodeSchema.Remove("required");
        }
        nodeSchema["required"] = required;

        // For child-capable transformation nodes, add/replace "transformations" property
        if (descriptor.SupportsChildren)
        {
            properties["transformations"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["$ref"] = "#/$defs/TransformationNode" }
            };
        }

        // Hoist nested $defs/definitions to root level with namespaced keys
        HoistNestedDefs(nodeSchema, rootDefs, qualifiedName, "$defs");
        HoistNestedDefs(nodeSchema, rootDefs, qualifiedName, "definitions");

        // Remove top-level schema properties that don't belong in a sub-schema
        nodeSchema.Remove("$schema");
        nodeSchema.Remove("$id");

        return nodeSchema;
    }

    private static void HoistNestedDefs(JsonObject nodeSchema, JsonObject rootDefs, string prefix, string defsKey)
    {
        if (nodeSchema[defsKey] is not JsonObject nestedDefs) return;

        foreach (var kvp in nestedDefs)
        {
            var namespacedKey = $"{prefix}_{kvp.Key}";
            if (kvp.Value != null)
            {
                var cloned = kvp.Value.DeepClone();
                RewriteRefs(cloned, $"#/{defsKey}/", "#/$defs/", prefix);
                rootDefs[namespacedKey] = cloned;
            }
        }

        // Rewrite internal $ref pointers in the node schema itself
        RewriteRefs(nodeSchema, $"#/{defsKey}/", "#/$defs/", prefix);

        nodeSchema.Remove(defsKey);
    }

    private static void RewriteRefs(JsonNode token, string oldRefPrefix, string newRefBase, string prefix)
    {
        switch (token)
        {
            case JsonObject obj:
            {
                if (obj["$ref"].AsString() is { } refStr && refStr.StartsWith(oldRefPrefix))
                {
                    var defName = refStr.Substring(oldRefPrefix.Length);
                    obj["$ref"] = $"{newRefBase}{prefix}_{defName}";
                }

                var keys = obj.Select(kvp => kvp.Key).ToList();
                foreach (var key in keys)
                {
                    if (obj[key] is { } child)
                    {
                        RewriteRefs(child, oldRefPrefix, newRefBase, prefix);
                    }
                }
                break;
            }
            case JsonArray arr:
            {
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        RewriteRefs(item, oldRefPrefix, newRefBase, prefix);
                    }
                }
                break;
            }
        }
    }
}
