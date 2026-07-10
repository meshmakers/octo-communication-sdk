using System.Collections.Concurrent;
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
/// Script globals surface. Argument values are supplied at execution time through
/// <see cref="Args"/> instead of being baked into the script text — this keeps the
/// compiled script identical across runs so it compiles once and is reused. The
/// members here are in scope as bare identifiers inside the compiled script.
/// </summary>
public sealed class ExecuteCSharpGlobals
{
    /// <summary>Per-execution argument values, keyed by argument name.</summary>
    public IReadOnlyDictionary<string, object?> Args = new Dictionary<string, object?>();
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
/// Executes inline C# code with typed arguments.
///
/// Arguments are passed as script <b>globals</b> (<see cref="ExecuteCSharpGlobals.Args"/>)
/// resolved at run time, NOT inlined as literals. The compiled script text therefore
/// only depends on the node's <see cref="ExecuteCSharpNodeConfiguration.Code"/> and its
/// argument signature — never on the values — so it is compiled exactly once and reused
/// for every subsequent execution. Baking values into the text (the previous behaviour)
/// produced a distinct script per changing value, defeating the cache and leaking a
/// compiled assembly per run (unbounded CPU + memory under a high-frequency pipeline).
/// </summary>
[NodeConfiguration(typeof(ExecuteCSharpNodeConfiguration))]
public class ExecuteCSharpNode(NodeDelegate next) : IPipelineNode
{
    /// <summary>
    /// Process-wide compiled-script cache, keyed by the full value-independent template
    /// text. The template depends only on the node's code, argument signature and usings
    /// — never on values, and never on machine-specific rtIds (those live in other nodes'
    /// configuration, not in the script) — so the SAME script used by N simulated
    /// machines / pipelines / tenants compiles exactly once and is shared. This makes the
    /// retained footprint scale with the number of DISTINCT scripts, not with the number
    /// of machines or executions (measured: 5 machines dropped from ~10GB with a
    /// per-context cache to a fraction of that once the identical scripts are shared).
    /// A compiled script holds only code; per-execution values flow in through
    /// <see cref="ExecuteCSharpGlobals"/>, so cross-tenant sharing carries no data.
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> + <see cref="Lazy{T}"/> give
    /// thread-safe compile-exactly-once without locking the pipeline data path.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<Script<object>>> CompiledScripts = new();

    /// <summary>
    /// Script options shared by every compiled script, built once. Resolving the whole
    /// loaded AppDomain into metadata references is not free, so doing it a single time
    /// instead of on every compile avoids repeated enumeration/resolution work. Imports
    /// are constant here; per-node <c>Usings</c> are emitted as <c>using</c> statements
    /// in the script text.
    ///
    /// NOTE: sharing the reference set does NOT meaningfully lower memory — measured on
    /// the maco simulator, the footprint is dominated by Roslyn binding all referenced
    /// assemblies into per-<see cref="Microsoft.CodeAnalysis.Compilation"/> symbol
    /// tables, which are retained per cached script and not shared. The lever for that
    /// is narrowing the reference set (e.g. framework-only cut ~2.2GB→~1.3GB for 12
    /// scripts) — deferred because it is a compatibility trade-off for scripts that
    /// reference application/domain assemblies.
    /// </summary>
    private static readonly Lazy<ScriptOptions> SharedScriptOptions = new(() =>
        ScriptOptions.Default
            .AddImports("System")
            .AddImports("System.Math")
            .AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location)));

    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ExecuteCSharpNodeConfiguration>();

        try
        {
            // Value-independent script template (declarations read from Args) — stable
            // across runs so the cache key is stable and compilation happens once.
            var scriptTemplate = BuildScriptTemplate(c);
            var script = GetOrCompileScript(scriptTemplate, nodeContext);

            // Resolve the actual values for this run and pass them via globals.
            var globals = new ExecuteCSharpGlobals { Args = BuildArgumentValues(dataContext, c, nodeContext) };

            using var cts = new CancellationTokenSource(c.TimeoutMs);
            var result = await script.RunAsync(globals, cancellationToken: cts.Token);

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

    private static Script<object> GetOrCompileScript(string scriptCode, INodeContext nodeContext)
    {
        // GetOrAdd may build the Lazy more than once under contention, but only the
        // stored one is ever resolved, and Lazy(ExecutionAndPublication) guarantees its
        // factory — the actual compilation — runs exactly once. Keyed by the full
        // template text so identical scripts across machines/pipelines share one compile.
        var lazy = CompiledScripts.GetOrAdd(scriptCode, code => new Lazy<Script<object>>(() =>
        {
            nodeContext.Debug("Compiling C# script");

            // Compile with the globals type so Args is in scope as a bare identifier.
            var script = CSharpScript.Create<object>(code, SharedScriptOptions.Value, typeof(ExecuteCSharpGlobals));
            var compilation = script.Compile();

            if (compilation.Any())
            {
                throw new CompilationErrorException("Compilation failed", compilation);
            }

            return script;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    /// <summary>Test seam: number of distinct scripts currently cached process-wide.</summary>
    internal static int CompiledScriptCacheCount => CompiledScripts.Count;

    /// <summary>Test seam: drop the process-wide compiled-script cache.</summary>
    internal static void ClearCompiledScriptCache() => CompiledScripts.Clear();

    /// <summary>
    /// Builds the value-independent script: usings, one declaration per argument that
    /// reads (and casts) its value from the <c>Args</c> globals dictionary, then the
    /// wrapped user code. Depends only on argument names/types + the user code.
    /// </summary>
    private static string BuildScriptTemplate(ExecuteCSharpNodeConfiguration c)
    {
        var script = new StringBuilder();

        script.AppendLine("#nullable disable");

        foreach (var usingStatement in c.Usings)
        {
            script.AppendLine($"using {usingStatement};");
        }

        if (c.Usings.Any())
        {
            script.AppendLine();
        }

        foreach (var arg in c.Arguments)
        {
            var typeName = GetCSharpTypeName(arg.DataType);
            // Value comes from globals at run time (never inlined). Missing/null coalesces
            // to the type's default, keeping the declared type non-nullable so user code
            // that uses the bare identifier (arithmetic, &&, …) still compiles.
            script.AppendLine(
                $"{typeName} {arg.Name} = ({typeName})(Args[\"{arg.Name}\"] ?? default({typeName}));");
        }

        script.AppendLine();

        var wrappedCode = WrapCode(c);
        script.AppendLine(wrappedCode);

        return script.ToString();
    }

    /// <summary>
    /// Resolves the actual per-run value for every argument into a name→value map that is
    /// handed to the script as globals. Every configured argument is always present (null
    /// when unresolved) so the generated <c>Args["name"]</c> lookups never throw.
    /// </summary>
    private Dictionary<string, object?> BuildArgumentValues(
        IDataContext dataContext, ExecuteCSharpNodeConfiguration c, INodeContext nodeContext)
    {
        var values = new Dictionary<string, object?>();

        foreach (var arg in c.Arguments)
        {
            object? value;

            if (!string.IsNullOrEmpty(arg.ValuePath))
            {
                // Resolve typed values directly via Get<T>() — under STJ, Get<object>()
                // returns a boxed JsonElement which does not implement IConvertible, so
                // Convert.ToInt32/etc would throw.
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
                value = arg.Value;
            }

            values[arg.Name] = ConvertArgumentValue(value, arg.DataType);
        }

        return values;
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
            AttributeValueTypesDto.String => Convert.ToString(value, CultureInfo.InvariantCulture),
            AttributeValueTypesDto.Int => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            AttributeValueTypesDto.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            AttributeValueTypesDto.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            AttributeValueTypesDto.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            AttributeValueTypesDto.DateTime => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
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
                AttributeValueTypesDto.String => Convert.ToString(result, CultureInfo.InvariantCulture),
                AttributeValueTypesDto.Int => Convert.ToInt32(result, CultureInfo.InvariantCulture),
                AttributeValueTypesDto.Int64 => Convert.ToInt64(result, CultureInfo.InvariantCulture),
                AttributeValueTypesDto.Boolean => Convert.ToBoolean(result, CultureInfo.InvariantCulture),
                AttributeValueTypesDto.Double => Convert.ToDouble(result, CultureInfo.InvariantCulture),
                AttributeValueTypesDto.DateTime => Convert.ToDateTime(result, CultureInfo.InvariantCulture),
                _ => result
            };
        }
        catch (Exception ex)
        {
            nodeContext.Error($"Failed to convert result to {returnType}: {ex.Message}");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Result conversion failed", ex);
        }
    }

    private static string GetCSharpTypeName(AttributeValueTypesDto dataType)
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
