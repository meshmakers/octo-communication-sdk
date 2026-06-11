using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Registry that lazily generates and caches JSON schemas for all registered pipeline node types.
/// Schemas are generated once per process lifetime using NJsonSchema.
/// </summary>
internal class NodeSchemaRegistry : INodeSchemaRegistry
{
    private readonly ConcurrentDictionary<string, NodeDescriptor> _descriptors = new();
    private readonly ConcurrentDictionary<string, XDocument?> _xmlDocsCache = new();
    private readonly List<NodeLookup> _nodeLookups;
    private readonly Dictionary<string, Type> _nodeConfigurations;
    private volatile bool _initialized;
    private readonly object _initLock = new();

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false
    };

    public NodeSchemaRegistry(List<NodeLookup> nodeLookups, Dictionary<string, Type> nodeConfigurations)
    {
        _nodeLookups = nodeLookups;
        _nodeConfigurations = nodeConfigurations;
    }

    /// <inheritdoc />
    public IReadOnlyList<NodeDescriptor> GetAllDescriptors()
    {
        EnsureInitialized();
        return _descriptors.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public NodeDescriptor? GetDescriptor(string qualifiedName)
    {
        EnsureInitialized();
        return _descriptors.TryGetValue(qualifiedName, out var descriptor) ? descriptor : null;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            // Process node lookups (nodes registered with both node type and configuration type)
            foreach (var lookup in _nodeLookups)
            {
                if (_descriptors.ContainsKey(lookup.QualifiedName)) continue;

                var descriptor = BuildDescriptor(lookup.QualifiedName, lookup.NodeConfigurationType, lookup.NodeType);
                _descriptors.TryAdd(lookup.QualifiedName, descriptor);
            }

            // Process standalone configurations (registered via RegisterNodeConfiguration without a node type)
            foreach (var kvp in _nodeConfigurations)
            {
                if (_descriptors.ContainsKey(kvp.Key)) continue;

                var descriptor = BuildDescriptor(kvp.Key, kvp.Value, nodeType: null);
                _descriptors.TryAdd(kvp.Key, descriptor);
            }

            _initialized = true;
        }
    }

    private NodeDescriptor BuildDescriptor(string qualifiedName, Type configType, Type? nodeType)
    {
        var nodeNameAttr = configType.GetCustomAttribute<NodeNameAttribute>();
        var nodeName = nodeNameAttr?.Name ?? qualifiedName;
        var version = nodeNameAttr?.Version ?? 1;

        var isTrigger = typeof(ITriggerNodeConfiguration).IsAssignableFrom(configType);
        var supportsChildren = typeof(IChildNodeConfiguration).IsAssignableFrom(configType);

        var category = DeriveCategory(configType, nodeType, isTrigger);

        string schemaJson;
        try
        {
            var settings = new SystemTextJsonSchemaGeneratorSettings
            {
                FlattenInheritanceHierarchy = true,
                SerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                }
            };
            var schema = JsonSchema.FromType(configType, settings);
            schemaJson = ConvertEnumsToConstantCase(schema.ToJson());
            schemaJson = RemoveAdditionalPropertiesConstraint(schemaJson);
            schemaJson = SimplifyNullableOneOf(schemaJson);
            schemaJson = SimplifyJsonConverterTypes(schemaJson, configType);
            schemaJson = FixTimeSpanFormat(schemaJson, configType);
            schemaJson = AddRequiredForNonNullableValueTypes(schemaJson, configType);
            schemaJson = InjectXmlDescriptions(schemaJson, configType);
            schemaJson = InjectPropertyGroupExtensions(schemaJson, configType);
            schemaJson = InjectNodeKindExtension(schemaJson, configType);
        }
        catch (Exception)
        {
            // If schema generation fails, provide a minimal schema
            schemaJson = "{}";
        }

        return new NodeDescriptor(nodeName, version, category, isTrigger, supportsChildren, schemaJson);
    }

    private static JsonObject ParseObject(string schemaJson)
    {
        var node = JsonNode.Parse(schemaJson);
        return node as JsonObject ?? new JsonObject();
    }

    private static string SerializeObject(JsonObject obj)
    {
        return obj.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Injects description fields from C# XML documentation into the JSON schema.
    /// </summary>
    private string InjectXmlDescriptions(string schemaJson, Type configType)
    {
        var root = ParseObject(schemaJson);

        // Add type-level description
        var typeXmlDoc = LoadXmlDocumentation(configType.Assembly);
        var typeSummary = GetMemberSummary(typeXmlDoc, $"T:{configType.FullName}");
        if (typeSummary != null)
        {
            root["description"] = typeSummary;
        }

        // Add property-level descriptions
        if (root["properties"] is JsonObject props)
        {
            foreach (var prop in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var declaringType = prop.DeclaringType ?? configType;
                var propXmlDoc = LoadXmlDocumentation(declaringType.Assembly);
                var summary = GetMemberSummary(propXmlDoc, $"P:{declaringType.FullName}.{prop.Name}");
                if (summary == null) continue;

                var camelName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
                if (props[camelName] is JsonObject propSchema)
                {
                    propSchema["description"] = summary;
                }
            }
        }

        return SerializeObject(root);
    }

    /// <summary>
    /// Injects PropertyGroup attribute metadata as JSON Schema extension properties (x-group, x-order, x-input).
    /// These are consumed by the frontend property editor for layout and input component selection.
    /// </summary>
    private static string InjectPropertyGroupExtensions(string schemaJson, Type configType)
    {
        var root = ParseObject(schemaJson);

        if (root["properties"] is not JsonObject props) return schemaJson;

        var hasAnyGroup = false;

        foreach (var prop in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var groupAttr = prop.GetCustomAttribute<PropertyGroupAttribute>();
            if (groupAttr == null) continue;

            var camelName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (props[camelName] is not JsonObject propSchema) continue;

            propSchema["x-group"] = groupAttr.Group;
            propSchema["x-order"] = groupAttr.Order;

            if (groupAttr.InputHint != null)
            {
                propSchema["x-input"] = groupAttr.InputHint;
            }

            hasAnyGroup = true;
        }

        return hasAnyGroup ? SerializeObject(root) : schemaJson;
    }

    /// <summary>
    /// Injects the [NodeKind] attribute value as the node-level JSON Schema extension
    /// "x-nodeKind". Consumed by the graphical editor to recognize structural nodes
    /// (e.g. a collapsible group). No-op when the config type has no [NodeKind].
    /// </summary>
    private static string InjectNodeKindExtension(string schemaJson, Type configType)
    {
        var kind = configType.GetCustomAttribute<NodeKindAttribute>()?.Kind;
        if (string.IsNullOrEmpty(kind)) return schemaJson;

        var root = ParseObject(schemaJson);
        root["x-nodeKind"] = kind;
        return SerializeObject(root);
    }

    /// <summary>
    /// Loads and caches XML documentation for an assembly, searching next to the DLL and in the NuGet cache.
    /// </summary>
    private XDocument? LoadXmlDocumentation(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        if (assemblyName == null) return null;

        return _xmlDocsCache.GetOrAdd(assemblyName, _ =>
        {
            try
            {
                // Try next to the assembly DLL
                var location = assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    var xmlPath = Path.ChangeExtension(location, ".xml");
                    if (File.Exists(xmlPath))
                        return XDocument.Load(xmlPath);
                }

                // Try NuGet global packages cache
                var nugetFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages");

                var packageDir = Path.Combine(nugetFolder, assemblyName.ToLowerInvariant());
                if (Directory.Exists(packageDir))
                {
                    foreach (var xmlFile in Directory.EnumerateFiles(
                                 packageDir, assemblyName + ".xml", SearchOption.AllDirectories))
                    {
                        return XDocument.Load(xmlFile);
                    }
                }

                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Extracts the summary text for a given member from an XML documentation file.
    /// </summary>
    private static string? GetMemberSummary(XDocument? xmlDoc, string memberName)
    {
        if (xmlDoc == null) return null;

        var member = xmlDoc.Descendants("member")
            .FirstOrDefault(m => m.Attribute("name")?.Value == memberName);

        var summary = member?.Element("summary")?.Value;
        if (string.IsNullOrWhiteSpace(summary)) return null;

        // Normalize whitespace (XML docs often have leading/trailing whitespace and newlines)
        return string.Join(" ", summary!.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Replaces complex object schemas with "type": "string" for properties whose C# types
    /// have a [JsonConverter] attribute that serializes them as strings.
    /// </summary>
    private static string SimplifyJsonConverterTypes(string schemaJson, Type configType)
    {
        var root = ParseObject(schemaJson);

        if (root["properties"] is not JsonObject props) return schemaJson;

        foreach (var prop in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = prop.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

            var camelName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (props[camelName] is not JsonObject propSchema) continue;

            // Check if it's a collection of a type with JsonConverter (on type or property)
            if (underlyingType.IsGenericType &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(underlyingType))
            {
                var elementType = underlyingType.GetGenericArguments().FirstOrDefault();
                if (elementType != null &&
                    (HasJsonConverterForType(elementType) || prop.GetCustomAttribute<JsonConverterAttribute>() != null))
                {
                    // Replace array items with string type
                    if (propSchema["items"] is JsonObject items)
                    {
                        // Clear existing keys
                        var keys = items.Select(kvp => kvp.Key).ToList();
                        foreach (var k in keys)
                        {
                            items.Remove(k);
                        }
                        items["type"] = "string";
                    }

                    continue;
                }
            }

            // Check if the type itself or the property has a JsonConverter
            if (!HasJsonConverterForType(underlyingType) &&
                prop.GetCustomAttribute<JsonConverterAttribute>() == null) continue;

            var isNullable = Nullable.GetUnderlyingType(propType) != null || !propType.IsValueType;
            var desc = propSchema["description"].AsString();

            // Clear all existing keys
            var allKeys = propSchema.Select(kvp => kvp.Key).ToList();
            foreach (var k in allKeys)
            {
                propSchema.Remove(k);
            }
            propSchema["type"] = isNullable
                ? new JsonArray("null", "string")
                : (JsonNode)JsonValue.Create("string")!;

            if (desc != null)
            {
                propSchema["description"] = desc;
            }
        }

        return SerializeObject(root);
    }

    private static readonly ConcurrentDictionary<Type, bool> ConverterCache = new();

    private static bool HasJsonConverterForType(Type type)
    {
        return ConverterCache.GetOrAdd(type, t =>
        {
            // Direct [JsonConverter] attribute on the type
            if (t.GetCustomAttribute<JsonConverterAttribute>() != null)
                return true;

            // Scan the type's assembly for a JsonConverter<T> implementation
            try
            {
                var targetConverterBase = typeof(JsonConverter<>).MakeGenericType(t);
                foreach (var candidate in t.Assembly.GetExportedTypes())
                {
                    if (!candidate.IsAbstract && !candidate.IsInterface &&
                        targetConverterBase.IsAssignableFrom(candidate))
                        return true;
                }
            }
            catch
            {
                // Ignore reflection failures
            }

            return false;
        });
    }

    /// <summary>
    /// Removes additionalProperties: false constraints from the schema.
    /// NJsonSchema generates these by default, but pipeline definitions may contain extra properties
    /// and the runtime deserializer is lenient.
    /// </summary>
    /// <summary>
    /// Removes "format": "duration" from TimeSpan properties. NJsonSchema generates
    /// ISO 8601 duration format (e.g. "PT10S") but .NET serializes TimeSpan as "hh:mm:ss"
    /// (e.g. "00:00:10"). The format constraint would cause false validation errors.
    /// </summary>
    private static string FixTimeSpanFormat(string schemaJson, Type configType)
    {
        var root = ParseObject(schemaJson);
        if (root["properties"] is not JsonObject props) return schemaJson;

        var changed = false;
        foreach (var prop in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (propType != typeof(TimeSpan)) continue;

            var camelName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (props[camelName] is JsonObject propSchema &&
                propSchema["format"].AsString() == "duration")
            {
                propSchema.Remove("format");
                changed = true;
            }
        }

        return changed ? SerializeObject(root) : schemaJson;
    }

    private static string RemoveAdditionalPropertiesConstraint(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root == null) return schemaJson;
        RemoveAdditionalPropertiesRecursive(root);
        return root.ToJsonString(WriteOptions);
    }

    private static void RemoveAdditionalPropertiesRecursive(JsonNode token)
    {
        if (token is JsonObject obj)
        {
            obj.Remove("additionalProperties");
            // Snapshot keys to avoid modification during enumeration
            var keys = obj.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                if (obj[key] is { } child)
                {
                    RemoveAdditionalPropertiesRecursive(child);
                }
            }
        }
        else if (token is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item != null)
                {
                    RemoveAdditionalPropertiesRecursive(item);
                }
            }
        }
    }

    /// <summary>
    /// Simplifies oneOf patterns generated by NJsonSchema for nullable object properties.
    /// NJsonSchema generates "oneOf": [{}, {"type": "null"}] for object? properties.
    /// Since the empty schema {} already matches everything (including null), both alternatives
    /// match null values, violating oneOf's "exactly one" requirement. This method replaces
    /// such patterns with just {} (accepts any value).
    /// </summary>
    private static string SimplifyNullableOneOf(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root == null) return schemaJson;
        SimplifyNullableOneOfRecursive(root);
        return root.ToJsonString(WriteOptions);
    }

    private static void SimplifyNullableOneOfRecursive(JsonNode token)
    {
        if (token is JsonObject obj)
        {
            // Check if this object has a oneOf with an empty schema and a null type
            if (obj["oneOf"] is JsonArray oneOf && ContainsEmptySchemaWithNullType(oneOf))
            {
                obj.Remove("oneOf");
            }

            var keys = obj.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                if (obj[key] is { } child)
                {
                    SimplifyNullableOneOfRecursive(child);
                }
            }
        }
        else if (token is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item != null)
                {
                    SimplifyNullableOneOfRecursive(item);
                }
            }
        }
    }

    private static bool ContainsEmptySchemaWithNullType(JsonArray oneOf)
    {
        var hasEmptySchema = false;
        var hasNullType = false;

        foreach (var item in oneOf)
        {
            if (item is JsonObject itemObj)
            {
                if (itemObj.Count == 0)
                {
                    hasEmptySchema = true;
                }
                else if (itemObj.Count == 1 && itemObj["type"].AsString() == "null")
                {
                    hasNullType = true;
                }
            }
        }

        return hasEmptySchema && hasNullType;
    }

    /// <summary>
    /// Converts enum schemas to CONSTANT_CASE string enums using x-enumNames.
    /// Handles both integer-based and string-based enums from NJsonSchema,
    /// deduplicates aliases (preferring the longer name), and converts to UPPER_SNAKE_CASE.
    /// </summary>
    private static string ConvertEnumsToConstantCase(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root == null) return schemaJson;
        ConvertEnumsRecursive(root);
        return root.ToJsonString(WriteOptions);
    }

    private static void ConvertEnumsRecursive(JsonNode token)
    {
        if (token is JsonObject obj)
        {
            if (obj["x-enumNames"] is JsonArray enumNames && obj["enum"] is JsonArray enumValues)
            {
                // Pair x-enumNames with enum values to detect aliases (same value, different names)
                var names = enumNames
                    .Select(n => n.AsString() ?? n?.ToString() ?? string.Empty)
                    .ToList();

                // Keep all alias names (e.g. both "Int" and "Integer") for backward compatibility
                var distinctNames = names.Distinct().ToList();

                // Build combined enum: PascalCase + CONSTANT_CASE (deduplicated)
                var allValues = distinctNames
                    .SelectMany(n => new[] { n, PascalToConstantCase(n) })
                    .Distinct()
                    .ToList();

                // Ensure type is "string"
                var typeToken = obj["type"];
                if (typeToken is JsonArray typeArr)
                {
                    for (var i = 0; i < typeArr.Count; i++)
                    {
                        if (typeArr[i].AsString() == "integer")
                            typeArr[i] = "string";
                    }
                }
                else if (typeToken.AsString() == "integer")
                {
                    obj["type"] = "string";
                }

                var newEnum = new JsonArray();
                foreach (var v in allValues)
                {
                    newEnum.Add(v);
                }
                obj["enum"] = newEnum;

                // Remove x-enumNames as it's no longer in sync after alias dedup and CONSTANT_CASE conversion
                obj.Remove("x-enumNames");
            }

            var keys = obj.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                if (obj[key] is { } child)
                {
                    ConvertEnumsRecursive(child);
                }
            }
        }
        else if (token is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item != null)
                {
                    ConvertEnumsRecursive(item);
                }
            }
        }
    }

    /// <summary>
    /// Converts a PascalCase string to UPPER_SNAKE_CASE (CONSTANT_CASE).
    /// Examples: Insert → INSERT, NotEquals → NOT_EQUALS, LessEqualsThan → LESS_EQUALS_THAN
    /// </summary>
    private static string PascalToConstantCase(string pascalCase)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascalCase[i]) && char.IsLower(pascalCase[i - 1]))
                sb.Append('_');
            sb.Append(char.ToUpperInvariant(pascalCase[i]));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Marks non-nullable value-type properties (structs like OctoObjectId) as required
    /// in the JSON schema. NJsonSchema doesn't do this automatically because struct properties
    /// always have a default value, but semantically they are mandatory configuration fields.
    /// Skips primitive types (int, bool, etc.) and enums since those have meaningful defaults.
    /// </summary>
    private static string AddRequiredForNonNullableValueTypes(string schemaJson, Type configType)
    {
        var root = ParseObject(schemaJson);
        if (root["properties"] is not JsonObject props) return schemaJson;

        var required = root["required"] as JsonArray ?? new JsonArray();
        var existingRequired = new HashSet<string>(
            required.Select(t => t.AsString() ?? t?.ToString() ?? string.Empty));
        var changed = false;

        foreach (var prop in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = prop.PropertyType;

            // Skip nullable types — they are intentionally optional
            if (Nullable.GetUnderlyingType(propType) != null) continue;

            // Skip if already marked [Required]
            if (prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() != null) continue;

            // Only target non-primitive value types (structs like OctoObjectId)
            if (!propType.IsValueType || propType.IsPrimitive || propType.IsEnum || propType == typeof(decimal)) continue;

            // Skip common System value types that have meaningful defaults
            if (propType == typeof(DateTime) || propType == typeof(DateTimeOffset) ||
                propType == typeof(TimeSpan) || propType == typeof(Guid)) continue;

            var camelName = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (props[camelName] != null && !existingRequired.Contains(camelName))
            {
                required.Add(camelName);
                existingRequired.Add(camelName);
                changed = true;
            }
        }

        if (changed)
        {
            // Replace required array (detach first to avoid InvalidOperationException about parent)
            if (root["required"] != null)
            {
                root.Remove("required");
            }
            root["required"] = required;
        }

        return SerializeObject(root);
    }

    private static string DeriveCategory(Type configType, Type? nodeType, bool isTrigger)
    {
        if (isTrigger) return "Trigger";

        // Try to derive from the node type's namespace first (more specific)
        var ns = nodeType?.Namespace ?? configType.Namespace ?? string.Empty;

        if (ns.Contains(".Triggers") || ns.Contains(".Trigger")) return "Trigger";
        if (ns.Contains(".Transforms") || ns.Contains(".Transform")) return "Transform";
        if (ns.Contains(".Loads") || ns.Contains(".Load")) return "Load";
        if (ns.Contains(".Extracts") || ns.Contains(".Extract")) return "Extract";
        if (ns.Contains(".Control")) return "Control";
        if (ns.Contains(".Buffering")) return "Buffering";

        return "Other";
    }
}
