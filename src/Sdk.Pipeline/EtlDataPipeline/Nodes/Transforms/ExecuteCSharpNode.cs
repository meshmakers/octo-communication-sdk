using System.Collections;
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
    /// Process-wide cache of compiled script <b>delegates</b>, keyed by the full
    /// value-independent template text. The template depends only on the node's code,
    /// argument signature and usings — never on values, and never on machine-specific
    /// rtIds (those live in other nodes' configuration, not in the script) — so the SAME
    /// script used by N simulated machines / pipelines / tenants compiles exactly once and
    /// is shared. This makes the retained footprint scale with the number of DISTINCT
    /// scripts, not with the number of machines or executions (measured: 5 machines
    /// dropped from ~10GB with a per-context cache to a fraction of that once the
    /// identical scripts are shared).
    /// <para>
    /// The value is a <see cref="ScriptRunner{T}"/>, deliberately <b>not</b> a
    /// <c>Script&lt;object&gt;</c>: holding the Script would pin its entire Roslyn
    /// Compilation graph, which cost ~61 MB of resident memory per distinct script and
    /// caused node-wide OOMs on prod-1 (AB#5448). See <c>GetOrCompileScript</c>.
    /// </para>
    /// A compiled script holds only code; per-execution values flow in through
    /// <see cref="ExecuteCSharpGlobals"/>, so cross-tenant sharing carries no data.
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> + <see cref="Lazy{T}"/> give
    /// thread-safe compile-exactly-once without locking the pipeline data path.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<ScriptRunner<object>>> CompiledScripts = new();

    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<ExecuteCSharpNodeConfiguration>();

        try
        {
            // Value-independent script template (declarations read from Args) — stable
            // across runs so the cache key is stable and compilation happens once.
            var scriptTemplate = BuildScriptTemplate(c);
            var runner = GetOrCompileScript(scriptTemplate, nodeContext);

            // Resolve the actual values for this run and pass them via globals.
            var globals = new ExecuteCSharpGlobals { Args = BuildArgumentValues(dataContext, c, nodeContext) };

            using var cts = new CancellationTokenSource(c.TimeoutMs);
            var returnValue = await runner(globals, cts.Token);

            var convertedResult = ConvertResult(returnValue, c.ReturnType, nodeContext);
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
        catch (PipelineExecutionException)
        {
            // Already a clear, node-scoped failure (e.g. invalid argument name) — surface
            // it as-is instead of re-wrapping it as a generic "script execution failed".
            throw;
        }
        catch (Exception ex)
        {
            nodeContext.Error($"Script execution failed: {ex.Message}\nStackTrace: {ex.StackTrace}");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Script execution failed", ex);
        }

        await next(dataContext, nodeContext);
    }

    private static ScriptRunner<object> GetOrCompileScript(string scriptCode, INodeContext nodeContext)
    {
        // GetOrAdd may build the Lazy more than once under contention, but only the
        // stored one is ever resolved, and Lazy(ExecutionAndPublication) guarantees its
        // factory — the actual compilation — runs exactly once. Keyed by the full
        // template text so identical scripts across machines/pipelines share one compile.
        var lazy = CompiledScripts.GetOrAdd(scriptCode, code => new Lazy<ScriptRunner<object>>(() =>
        {
            nodeContext.Debug("Compiling C# script");

            // Options (imports + the full loaded-AppDomain reference set) are built HERE,
            // inside the compile factory, so they are resolved once per DISTINCT script at
            // the moment it first compiles — i.e. only on a cache miss, which after warmup
            // is bounded by the number of distinct scripts and is rare. This keeps the
            // enumeration off the per-execution hot path (the Lazy factory runs exactly
            // once per template) while ensuring a script compiled later sees the AppDomain's
            // CURRENT assemblies — a reference to an assembly loaded after an earlier compile
            // resolves correctly, instead of being frozen out by a build-once shared set.
            // The full AppDomain set (not a framework-only subset) is kept deliberately for
            // backward compatibility with scripts that reference application/domain assemblies.
            var scriptOptions = ScriptOptions.Default
                .AddImports("System")
                .AddImports("System.Math")
                .AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => a.Location));

            // Compile with the globals type so Args is in scope as a bare identifier.
            var script = CSharpScript.Create<object>(code, scriptOptions, typeof(ExecuteCSharpGlobals));
            var compilation = script.Compile();

            if (compilation.Any())
            {
                throw new CompilationErrorException("Compilation failed", compilation);
            }

            // Cache the DELEGATE, not the Script (AB#5448). A Script<T> keeps its whole
            // Roslyn Compilation graph alive — syntax trees, symbol tables and a
            // MetadataReference per referenced assembly — and that state lives largely in
            // unmanaged/mmapped metadata buffers. Measured cost: ~61 MB of resident memory
            // per DISTINCT cached script, so the 43 ExecuteCSharp nodes of the accounting
            // blueprint pinned ~2.8 GB against a 3Gi container limit and drove node-wide
            // OOMs on prod-1. CreateDelegate() hands back the emitted assembly plus its
            // entry point; dropping the Script reference here lets the compilation graph be
            // collected and takes the same 43 scripts down to ~700 MB, where it plateaus.
            // Cache semantics are unchanged: still keyed by the value-independent template,
            // still compiled exactly once, same CPU profile. Note that this memory is
            // invisible to `dotnet.assembly.count` — MetadataReferences are unmanaged
            // metadata readers, not loaded assemblies — which is why it went unnoticed.
            return script.CreateDelegate();
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
            // The name is emitted as a bare C# identifier and as a dictionary key, so a
            // name that is not a valid identifier would otherwise surface as a confusing
            // Roslyn compile error (or break the generated string). Fail fast with a clear
            // message instead. Argument values themselves are never inlined (they flow via
            // Args at run time), so there is no value-injection surface here.
            if (!IsValidCSharpIdentifier(arg.Name))
            {
                throw new PipelineExecutionException(
                    $"ExecuteCSharp argument name '{arg.Name}' is not a valid C# identifier.");
            }

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
                    value = ResolveTypedFromPath(dataContext, arg, nodeContext);
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

    /// <summary>
    /// Resolves one argument from the data context. The strict typed read is always tried
    /// first, so the fast path and every existing behaviour are unchanged; only when the
    /// deserializer rejects the JSON value because its type does not match the declared
    /// <see cref="ScriptArgument.DataType"/> does the lenient fallback run (AB#5463).
    /// </summary>
    private static object? ResolveTypedFromPath(IDataContext dataContext, ScriptArgument arg, INodeContext nodeContext)
    {
        var path = arg.ValuePath!;
        try
        {
            return ResolveTypedStrict(dataContext, path, arg.DataType);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // Deliberately narrow: JsonException is STJ's "the JSON value could not be
            // converted to T" (a number token read as string, an object read as double, an
            // out-of-range integer from the parity converters); FormatException is what a
            // converter's own invariant Parse raises for a non-numeric string. Anything else
            // — a bug in the data context, a broken path expression — must keep surfacing.
            return ResolveLeniently(dataContext, arg, path, nodeContext, ex);
        }
    }

    /// <summary>
    /// AB#5463: a JSON value whose type does not match the declared argument type used to
    /// throw INSIDE argument resolution — before the script ran — so no <c>try</c>/<c>catch</c>
    /// in the script could see it, and with no <c>continueOnError</c> on <c>ExecuteCSharp@1</c>
    /// or the per-item <c>ForEach@1</c> the whole iteration died with a generic
    /// "Script execution failed". The trigger in production was an LLM emitting an invoice
    /// number as an unquoted JSON number for an argument declared <c>String</c>.
    /// <para>
    /// The coercion lives HERE and not in <see cref="SystemTextJsonOptions.Default"/> on purpose:
    /// those options are the one bundle every node and every adapter deserializes with, so a
    /// string converter there would silently change behaviour far outside this node (and a
    /// number→string coercion has no Newtonsoft-parity oracle to pin it against).
    /// </para>
    /// Conversions are invariant-culture only. <c>DateTime</c> is deliberately NOT lenient:
    /// STJ already accepts every ISO 8601 string, and anything it rejects (<c>"01.02.2026"</c>,
    /// a bare Unix number) is ambiguous — <c>DateTime.Parse</c> under the invariant culture
    /// would happily read that as February 1st or January 2nd depending on the format it
    /// guesses, which is a worse outcome than the clear error produced here.
    /// </summary>
    private static object? ResolveLeniently(
        IDataContext dataContext, ScriptArgument arg, string path, INodeContext nodeContext, Exception cause)
    {
        // JsonElement deserializes from any JSON kind, so this read cannot fail on a type
        // mismatch — it only tells us what is actually there.
        var element = dataContext.Get<JsonElement>(path);
        var found = element.ValueKind;

        if (TryConvertLeniently(element, arg.DataType, out var converted))
        {
            // A pipeline quietly relying on coercion should be visible in the log: name the
            // argument and both types so the producer (or the declared dataType) can be fixed.
            nodeContext.Warning(
                $"Argument '{arg.Name}' is declared {arg.DataType} but the value at '{path}' is a JSON {found}; " +
                $"converted it to {arg.DataType} with the invariant culture (AB#5463). " +
                "Fix the producer or the argument's dataType — this fallback is not a contract.");
            return converted;
        }

        // A silent null here would be worse than the original throw: fail, but name the
        // argument, the path, the declared type and what was actually found.
        throw new PipelineExecutionException(
            $"[{nodeContext.NodePath}]: Argument '{arg.Name}' is declared {arg.DataType} but the value at " +
            $"'{path}' is a JSON {found} that cannot be converted to {arg.DataType}: {cause.Message}",
            cause);
    }

    /// <summary>
    /// Lenient scalar/array conversion for a JSON value of the wrong kind. Returns false for
    /// every combination that is not unambiguously convertible (objects, arrays where a scalar
    /// is declared, non-numeric strings, booleans into numbers, anything into DateTime).
    /// </summary>
    private static bool TryConvertLeniently(JsonElement element, AttributeValueTypesDto dataType, out object? value)
    {
        value = null;
        switch (dataType)
        {
            case AttributeValueTypesDto.String:
                switch (element.ValueKind)
                {
                    case JsonValueKind.Number:
                        // The raw token text IS the invariant representation (JSON numbers
                        // have no culture), and it preserves the digits exactly as emitted —
                        // "20260001" stays "20260001", not "2.0260001E+07".
                        value = element.GetRawText();
                        return true;
                    case JsonValueKind.True:
                        value = "true";
                        return true;
                    case JsonValueKind.False:
                        value = "false";
                        return true;
                    default:
                        return false;
                }

            case AttributeValueTypesDto.Double:
                if (element.ValueKind == JsonValueKind.String &&
                    double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    value = d;
                    return true;
                }
                return false;

            case AttributeValueTypesDto.Int:
                if (TryParseIntegralString(element, out var l) && l is >= int.MinValue and <= int.MaxValue)
                {
                    value = (int)l;
                    return true;
                }
                return false;

            case AttributeValueTypesDto.Int64:
                if (TryParseIntegralString(element, out var l64))
                {
                    value = l64;
                    return true;
                }
                return false;

            case AttributeValueTypesDto.Boolean:
                if (element.ValueKind == JsonValueKind.String &&
                    bool.TryParse(element.GetString(), out var b))
                {
                    value = b;
                    return true;
                }
                return false;

            case AttributeValueTypesDto.StringArray:
                return TryConvertArrayLeniently<string>(element, AttributeValueTypesDto.String, out value);

            case AttributeValueTypesDto.IntArray:
                return TryConvertArrayLeniently<int>(element, AttributeValueTypesDto.Int, out value);

            // DateTime (see ResolveLeniently) and everything else: strict.
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads a JSON string as an integral value with the invariant culture: a plain integer,
    /// or a real whose value is integral (<c>"5.0"</c>). A fractional string is not an integer
    /// and is rejected rather than rounded — the banker's rounding of AB#5275 applies to JSON
    /// numbers the data context reads, not to text somebody typed.
    /// </summary>
    private static bool TryParseIntegralString(JsonElement element, out long value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var s = element.GetString();
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
            Math.Floor(d) == d && d is >= long.MinValue and <= long.MaxValue)
        {
            value = (long)d;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Element-wise lenient conversion for the typed array kinds: each element is read
    /// strictly first and falls back to <see cref="TryConvertLeniently"/> only on a mismatch.
    /// A non-array where an array is declared is never converted.
    /// </summary>
    private static bool TryConvertArrayLeniently<T>(JsonElement element, AttributeValueTypesDto elementType, out object? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var result = new List<T>();
        foreach (var item in element.EnumerateArray())
        {
            try
            {
                var strict = item.Deserialize<T>(SystemTextJsonOptions.Default);
                if (strict is null)
                {
                    return false;
                }
                result.Add(strict);
                continue;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                // fall through to the lenient element conversion
            }

            if (!TryConvertLeniently(item, elementType, out var converted) || converted is not T typed)
            {
                return false;
            }
            result.Add(typed);
        }

        value = result.ToArray();
        return true;
    }

    private static object? ResolveTypedStrict(IDataContext dataContext, string path, AttributeValueTypesDto dataType)
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
            // Array types (AB#5232): deserialize straight to the typed CLR array so the
            // script sees string[]/int[] instead of a boxed JsonElement. RecordArray keeps
            // the generic path (elements are complex objects) and is materialized to
            // object[] in ConvertArgumentValue.
            AttributeValueTypesDto.StringArray => dataContext.Get<string[]>(path),
            AttributeValueTypesDto.IntArray => dataContext.Get<int[]>(path),
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
            // Array types (AB#5232): whatever shape the value arrives in (typed array from
            // ResolveTypedFromPath, JsonElement/JsonNode array, or a native list from a
            // fixed configuration Value), the script always receives the declared CLR array.
            AttributeValueTypesDto.StringArray =>
                MaterializeArray(value, e => Convert.ToString(e, CultureInfo.InvariantCulture)!),
            AttributeValueTypesDto.IntArray =>
                MaterializeArray(value, e => Convert.ToInt32(e, CultureInfo.InvariantCulture)),
            AttributeValueTypesDto.RecordArray => MaterializeArray<object?>(value, e => e),
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
                // Array return types (AB#5232): a script may return T[], List<T> or a lazy
                // LINQ enumerable — materialize all of them to the declared CLR array so the
                // value written to the data context is a proper JSON array of the right type.
                AttributeValueTypesDto.StringArray =>
                    MaterializeArray(result, e => Convert.ToString(e, CultureInfo.InvariantCulture)!),
                AttributeValueTypesDto.IntArray =>
                    MaterializeArray(result, e => Convert.ToInt32(e, CultureInfo.InvariantCulture)),
                AttributeValueTypesDto.RecordArray => MaterializeArray<object?>(result, e => e),
                _ => result
            };
        }
        catch (Exception ex)
        {
            nodeContext.Error($"Failed to convert result to {returnType}: {ex.Message}");
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Result conversion failed", ex);
        }
    }

    /// <summary>
    /// Materializes an incoming array-typed value into a typed CLR array (AB#5232).
    /// Accepts every shape an array argument can arrive in: an already-typed array,
    /// a boxed <see cref="JsonElement"/>/<see cref="JsonNode"/> array (values resolved
    /// from the data context), or a native list (fixed configuration values, script
    /// return values including lazy LINQ enumerables).
    /// </summary>
    private static T[] MaterializeArray<T>(object value, Func<object?, T> convertElement)
    {
        if (value is T[] typedArray)
        {
            return typedArray;
        }

        if (value is JsonNode node)
        {
            value = node.Deserialize<JsonElement>(SystemTextJsonOptions.Default);
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"Expected a JSON array for an array-typed argument, but got {element.ValueKind}.");
            }

            return element.EnumerateArray().Select(e => convertElement(JsonElementToClr(e))).ToArray();
        }

        if (value is string)
        {
            // string is IEnumerable<char> — never a valid array value; fail clearly.
            throw new InvalidOperationException(
                "Expected an array value for an array-typed argument, but got a string.");
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>()
                .Select(o => convertElement(o is JsonElement je ? JsonElementToClr(je) : o))
                .ToArray();
        }

        throw new InvalidOperationException(
            $"Cannot materialize a value of type {value.GetType().Name} into an array.");
    }

    /// <summary>
    /// Unwraps a scalar <see cref="JsonElement"/> to its CLR value so element conversion
    /// (<see cref="Convert.ToString(object?, IFormatProvider?)"/> etc.) works — JsonElement
    /// itself is not <see cref="IConvertible"/>. Complex elements (objects/arrays, i.e.
    /// RecordArray members) stay JsonElement.
    /// </summary>
    private static object? JsonElementToClr(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element
        };
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
            // Array types (AB#5232): declare real CLR arrays so scripts can foreach/LINQ
            // over the argument directly. These are ALL the array kinds the CK type system
            // defines (AttributeValueTypesDto) — there is no Double/Boolean/DateTime/Int64
            // array in the enum. IntArray covers the IntegerArray alias (same value).
            AttributeValueTypesDto.StringArray => "string[]",
            AttributeValueTypesDto.IntArray => "int[]",
            AttributeValueTypesDto.RecordArray => "object[]",
            _ => "object"
        };
    }

    private static bool IsValidCSharpIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
