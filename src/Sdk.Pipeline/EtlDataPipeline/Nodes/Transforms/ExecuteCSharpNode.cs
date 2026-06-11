using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;


/// <summary>
/// Argument configuration for passing values to C# script
/// </summary>
public record ScriptArgument
{
    /// <summary>
    /// Name of the variable in the C# script
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// JSON path to get the value (e.g. "$.demo.path")
    /// </summary>
    public string? ValuePath { get; set; }

    /// <summary>
    /// Fixed value to use (alternative to ValuePath)
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Data type of the argument
    /// </summary>
    public required AttributeValueTypesDto DataType { get; set; }
}

/// <summary>
/// Configuration for executing C# code
/// </summary>
[NodeName("ExecuteCSharp", 1)]
public record ExecuteCSharpNodeConfiguration : TargetPathNodeConfiguration
{
    /// <summary>
    /// The C# code to execute. Should return a value.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required string Code { get; set; }

    /// <summary>
    /// List of arguments to pass to the script
    /// </summary>
    [PropertyGroup("Data Mapping", 0)]
    public IEnumerable<ScriptArgument> Arguments { get; set; } = new List<ScriptArgument>();

    /// <summary>
    /// Return type of the script
    /// </summary>
    [PropertyGroup("Output", 0)]
    public required AttributeValueTypesDto ReturnType { get; set; }

    /// <summary>
    /// Timeout in milliseconds (default: 5000ms)
    /// </summary>
    [PropertyGroup("Timing", 0)]
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Additional using statements (e.g. "System.Linq")
    /// </summary>
    [PropertyGroup("Options", 1)]
    public IEnumerable<string> Usings { get; set; } = new List<string>();
}

/// <summary>
/// Executes inline C# code with typed arguments
/// </summary>
[NodeConfiguration(typeof(ExecuteCSharpNodeConfiguration))]
public class ExecuteCSharpNode(NodeDelegate next, IEtlContext etlContext) : IPipelineNode
{
    private const string CacheKeyPrefix = "ExecuteCSharpNode_CompiledScript_";

    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ExecuteCSharpNodeConfiguration>();

        try
        {
            // Build script with actual values
            var scriptCode = BuildScriptWithValues(dataContext, c, nodeContext);

            // Get or compile the script
            var script = await GetOrCompileScriptAsync(scriptCode, nodeContext);

            // Execute with timeout
            using var cts = new CancellationTokenSource(c.TimeoutMs);
            var result = await script.RunAsync(cancellationToken: cts.Token);

            // Convert and set result
            var convertedResult = ConvertResult(result.ReturnValue, c.ReturnType, nodeContext);
            dataContext.Set(c.TargetPath, convertedResult, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
        }
        catch (CompilationErrorException ex)
        {
            var errorMessage = new StringBuilder($"C# compilation failed:");
            foreach (var diagnostic in ex.Diagnostics)
            {
                errorMessage.AppendLine($"  Line {diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");
            }
            nodeContext.Error(errorMessage.ToString());
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: {errorMessage}", ex);
        }
        catch (OperationCanceledException)
        {
            var error = $"Script execution timeout after {c.TimeoutMs}ms";
            nodeContext.Error(error);
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: {error}");
        }
        catch (Exception ex)
        {
            nodeContext.Error($"Script execution failed: {ex.Message}\nStackTrace: {ex.StackTrace}");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Script execution failed", ex);
        }

        await next(dataContext, nodeContext);
    }

    private Task<Script<object>> GetOrCompileScriptAsync(string scriptCode, INodeContext nodeContext)
    {
        var cacheKey = $"{CacheKeyPrefix}{scriptCode.GetHashCode()}";

        // Try to get from cache
        if (etlContext.Properties.TryGetValue(cacheKey, out var cached) && cached is Script<object> cachedScript)
        {
            nodeContext.Debug("Using cached compiled script");
            return Task.FromResult(cachedScript);
        }

        // Build the script options
        var scriptOptions = ScriptOptions.Default
            .AddImports("System")
            .AddImports("System.Math")
            .AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location));

        nodeContext.Debug($"Compiling C# script");

        // Compile the script without globals
        var script = CSharpScript.Create(scriptCode, scriptOptions);
        var compilation = script.Compile();

        if (compilation.Any())
        {
            throw new CompilationErrorException("Compilation failed", compilation);
        }

        // Cache the compiled script
        etlContext.Properties[cacheKey] = script;
        nodeContext.Debug("Script compiled and cached successfully");

        return Task.FromResult(script);
    }


    private string BuildScriptWithValues(IDataContext dataContext, ExecuteCSharpNodeConfiguration c, INodeContext nodeContext)
    {
        var script = new StringBuilder();

        // Add #nullable disable to avoid nullable reference type issues
        script.AppendLine("#nullable disable");

        // Add using statements
        foreach (var usingStatement in c.Usings)
        {
            script.AppendLine($"using {usingStatement};");
        }

        if (c.Usings.Any())
        {
            script.AppendLine();
        }

        // Add variable declarations with actual values
        foreach (var arg in c.Arguments)
        {
            object? value;

            if (!string.IsNullOrEmpty(arg.ValuePath))
            {
                // Get value from JSON path. Resolve typed values directly via Get<T>() —
                // under STJ, Get<object>() returns a boxed JsonElement which does not
                // implement IConvertible, so Convert.ToInt32/etc would throw.
                if (!dataContext.Exists(arg.ValuePath!) ||
                    dataContext.GetKind(arg.ValuePath!) == DataKind.Null)
                {
                    if (!dataContext.Exists(arg.ValuePath!))
                    {
                        nodeContext.Warning($"Path '{arg.ValuePath}' not found for argument '{arg.Name}', using null");
                    }
                    value = null;
                }
                else
                {
                    value = ResolveTypedFromPath(dataContext, arg.ValuePath!, arg.DataType);
                }
            }
            else
            {
                // Use fixed value
                value = arg.Value;
            }

            // Convert to target type
            var convertedValue = ConvertArgumentValue(value, arg.DataType);

            // Generate the variable declaration with the actual value
            var typeName = GetCSharpTypeName(arg.DataType);
            var valueString = FormatValueForCode(convertedValue, arg.DataType);

            // Handle null values properly by making the type nullable if needed
            if (convertedValue == null && arg.DataType != AttributeValueTypesDto.String)
            {
                typeName = typeName + "?";
            }

            script.AppendLine($"{typeName} {arg.Name} = {valueString};");
        }

        script.AppendLine();

        // Add the user code
        var wrappedCode = WrapCode(c);
        script.AppendLine(wrappedCode);

        return script.ToString();
    }

    private static string FormatValueForCode(object? value, AttributeValueTypesDto dataType)
    {
        if (value == null)
        {
            return "null";
        }

        return dataType switch
        {
            AttributeValueTypesDto.String => $"\"{value.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            AttributeValueTypesDto.Int => value.ToString()!,
            AttributeValueTypesDto.Int64 => $"{value}L",
            AttributeValueTypesDto.Boolean => value.ToString()!.ToLower(),
            AttributeValueTypesDto.Double => ((double)value).ToString("F", CultureInfo.InvariantCulture),
            AttributeValueTypesDto.DateTime => $"DateTime.Parse(\"{((DateTime)value):yyyy-MM-dd HH:mm:ss}\")",
            _ => $"\"{value}\""
        };
    }

    private static string WrapCode(ExecuteCSharpNodeConfiguration c)
    {
        // If the code already has a return statement, use it as-is
        if (c.Code.Contains("return"))
        {
            return c.Code;
        }

        // Otherwise, treat it as an expression and add return
        return $"return {c.Code};";
    }

    private static object? ResolveTypedFromPath(IDataContext dataContext, string path, AttributeValueTypesDto dataType)
    {
        // STJ deserializes the underlying JsonNode/JsonElement directly to the target
        // CLR type. This avoids the JsonElement-is-not-IConvertible problem.
        return dataType switch
        {
            AttributeValueTypesDto.String => dataContext.Get<string>(path),
            AttributeValueTypesDto.Int => (object?)dataContext.Get<int>(path),
            AttributeValueTypesDto.Int64 => (object?)dataContext.Get<long>(path),
            AttributeValueTypesDto.Boolean => (object?)dataContext.Get<bool>(path),
            AttributeValueTypesDto.Double => (object?)dataContext.Get<double>(path),
            AttributeValueTypesDto.DateTime => (object?)dataContext.Get<DateTime>(path),
            _ => dataContext.Get<JsonNode>(path)?.Deserialize<object?>(SystemTextJsonOptions.Default)
        };
    }

    private object? ConvertArgumentValue(object? value, AttributeValueTypesDto dataType)
    {
        if (value == null) return null;

        return dataType switch
        {
            AttributeValueTypesDto.String => Convert.ToString(value),
            AttributeValueTypesDto.Int => Convert.ToInt32(value),
            AttributeValueTypesDto.Int64 => Convert.ToInt64(value),
            AttributeValueTypesDto.Boolean => Convert.ToBoolean(value),
            AttributeValueTypesDto.Double => Convert.ToDouble(value),
            AttributeValueTypesDto.DateTime => Convert.ToDateTime(value),
            _ => value
        };
    }

    private object? ConvertResult(object? result, AttributeValueTypesDto returnType, INodeContext nodeContext)
    {
        if (result == null) return null;

        try
        {
            return returnType switch
            {
                AttributeValueTypesDto.String => Convert.ToString(result),
                AttributeValueTypesDto.Int => Convert.ToInt32(result),
                AttributeValueTypesDto.Int64 => Convert.ToInt64(result),
                AttributeValueTypesDto.Boolean => Convert.ToBoolean(result),
                AttributeValueTypesDto.Double => Convert.ToDouble(result),
                AttributeValueTypesDto.DateTime => Convert.ToDateTime(result),
                _ => result
            };
        }
        catch (Exception ex)
        {
            nodeContext.Error($"Failed to convert result to {returnType}: {ex.Message}");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Result conversion failed", ex);
        }
    }

    private string GetCSharpTypeName(AttributeValueTypesDto dataType)
    {
        return dataType switch
        {
            AttributeValueTypesDto.String => "string",
            AttributeValueTypesDto.Int => "int",
            AttributeValueTypesDto.Int64 => "long",
            AttributeValueTypesDto.Boolean => "bool",
            AttributeValueTypesDto.Double => "double",
            AttributeValueTypesDto.DateTime => "DateTime",
            _ => "object"
        };
    }
}