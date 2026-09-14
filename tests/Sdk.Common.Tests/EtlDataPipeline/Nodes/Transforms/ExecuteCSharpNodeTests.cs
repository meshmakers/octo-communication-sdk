using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class ExecuteCSharpNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(ExecuteCSharpNodeConfiguration configuration, JsonObject? testData = null,
        IServiceProvider? serviceProvider = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var data = testData ?? new JsonObject();
        var dataContext = new DataContextImpl(JsonDocument.Parse(data.ToJsonString()));
        var services = serviceProvider ?? fixture.Services.BuildServiceProvider();
        var rootNodeContext = NodeContext.CreateRootNodeContext(services, logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ExecuteCSharp", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_SimpleCalculation_ReturnsCorrectResult()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "2 + 2",
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(4, dataContext.Get<int>("$.result"));
    }

    // Phase 11 regression: ExecuteCSharpNode.ConvertArgumentValue uses Convert.ToInt32/etc
    // on values returned from dataContext (boxed JsonElement). JsonElement does not
    // implement IConvertible. Tests that resolve script arguments via ValuePath fail
    // with InvalidCastException. Production fix: use Get<targetType> per argument or
    // unwrap the JsonElement explicitly.
    [Fact]
    public async Task ProcessObjectAsync_IsPrimeFunction_ReturnsCorrectResult()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = @"
                int n = number;
                if (n <= 1) return false;
                for (int i = 2; i * i <= n; i++)
                {
                    if (n % i == 0) return false;
                }
                return true;
            ",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "number", ValuePath = "$.input", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Boolean,
            TargetPath = "$.isPrime"
        };
        var testData = new JsonObject { ["input"] = 17 };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.True(dataContext.Get<bool>("$.isPrime"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithFixedValue_UsesFixedValue()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "prefix + suffix",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "prefix", Value = "Hello, ", DataType = AttributeValueTypesDto.String },
                new() { Name = "suffix", ValuePath = "$.name", DataType = AttributeValueTypesDto.String }
            },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.greeting"
        };
        var testData = new JsonObject { ["name"] = "World" };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("Hello, World", dataContext.Get<string>("$.greeting"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithMathFunctions_CalculatesCorrectly()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "Math.Sqrt(value) + Math.Pow(2, 3)",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "value", ValuePath = "$.number", DataType = AttributeValueTypesDto.Double }
            },
            ReturnType = AttributeValueTypesDto.Double,
            TargetPath = "$.result"
        };
        var testData = new JsonObject { ["number"] = 16.0 };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(12.0, dataContext.Get<double>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithNullValue_HandlesNull()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "value == null ? \"NULL\" : value.ToString()",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "value", ValuePath = "$.missing", DataType = AttributeValueTypesDto.String }
            },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("NULL", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithMultipleArguments_CalculatesCorrectly()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "(a + b) * c",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "a", ValuePath = "$.x", DataType = AttributeValueTypesDto.Int },
                new() { Name = "b", ValuePath = "$.y", DataType = AttributeValueTypesDto.Int },
                new() { Name = "c", ValuePath = "$.z", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var testData = new JsonObject { ["x"] = 2, ["y"] = 3, ["z"] = 4 };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(20, dataContext.Get<int>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithCompilationError_ThrowsException()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "this is not valid C# code",
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(
            () => node.ProcessObjectAsync(dataContext, nodeContext)
        );
    }

    [Fact]
    public async Task ProcessObjectAsync_WithRuntimeException_ThrowsException()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "1 / zero",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "zero", Value = 0, DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(
            () => node.ProcessObjectAsync(dataContext, nodeContext)
        );
    }

    [Fact]
    public async Task ProcessObjectAsync_WithTimeout_ThrowsTimeoutException()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "while(true) { }",
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result",
            TimeoutMs = 100
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(
            () => node.ProcessObjectAsync(dataContext, nodeContext)
        );
    }

    [Fact]
    public async Task ProcessObjectAsync_WithCustomUsings_ImportsCorrectly()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "string.Join(\", \", new[] { \"a\", \"b\", \"c\" }.Select(s => s.ToUpper()))",
            Usings = new List<string> { "System.Linq" },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("A, B, C", dataContext.Get<string>("$.result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ScriptIsCached_UsesCachedVersion()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "counter + 1",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "counter", ValuePath = "$.count", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        var testData1 = new JsonObject { ["count"] = 5 };
        var (dataContext1, nodeContext1) = PrepareTest(config, testData1);
        await node.ProcessObjectAsync(dataContext1, nodeContext1);
        var result1 = dataContext1.Get<int>("$.result");

        var testData2 = new JsonObject { ["count"] = 10 };
        var (dataContext2, nodeContext2) = PrepareTest(config, testData2);
        await node.ProcessObjectAsync(dataContext2, nodeContext2);
        var result2 = dataContext2.Get<int>("$.result");

        Assert.Equal(6, result1);
        Assert.Equal(11, result2);
    }

    [Fact]
    public async Task ProcessObjectAsync_WithBooleanLogic_EvaluatesCorrectly()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "age >= 18 && hasLicense",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "age", ValuePath = "$.person.age", DataType = AttributeValueTypesDto.Int },
                new() { Name = "hasLicense", ValuePath = "$.person.license", DataType = AttributeValueTypesDto.Boolean }
            },
            ReturnType = AttributeValueTypesDto.Boolean,
            TargetPath = "$.canDrive"
        };
        var testData = new JsonObject
        {
            ["person"] = new JsonObject
            {
                ["age"] = 20,
                ["license"] = true
            }
        };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.True(dataContext.Get<bool>("$.canDrive"));
    }

    // Regression guard for the compile-once fix: argument values flow through script
    // globals at run time, so a node whose input changes every execution (like a
    // per-tick simulator counter) must still compile its script exactly ONCE and reuse
    // it. The previous implementation inlined values as literals, producing a distinct
    // script — and a leaked compiled assembly — per changing value, which pegged CPU
    // and grew memory without bound under a high-frequency pipeline.
    [Fact]
    public async Task ProcessObjectAsync_ChangingValues_CompilesOnceAndReuses()
    {
        ExecuteCSharpNode.ClearCompiledScriptCache();

        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "counter + 1 /* compiles-once */",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "counter", ValuePath = "$.count", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        for (var i = 0; i < 50; i++)
        {
            var (dataContext, nodeContext) = PrepareTest(config, new JsonObject { ["count"] = i });
            await node.ProcessObjectAsync(dataContext, nodeContext);
            Assert.Equal(i + 1, dataContext.Get<int>("$.result"));
        }

        Assert.Equal(1, ExecuteCSharpNode.CompiledScriptCacheCount);
    }

    // Regression guard for the compile cache race: the process-wide cache is a
    // ConcurrentDictionary whose entries are Lazy<Script> (ExecutionAndPublication).
    // When many executions of the same template run concurrently (parallel ForEach
    // iterations on a high-frequency pipeline), the compilation must still happen exactly
    // once — never a thread-race that compiles the same script repeatedly.
    [Fact]
    public async Task ProcessObjectAsync_ConcurrentExecutions_CompilesExactlyOnce()
    {
        ExecuteCSharpNode.ClearCompiledScriptCache();

        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "counter + 2 /* concurrent */",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "counter", ValuePath = "$.count", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        // Build the service provider once and share it across tasks so the test exercises
        // the script-cache race, not concurrent ServiceProvider construction.
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            var (dataContext, nodeContext) = PrepareTest(config, new JsonObject { ["count"] = i }, serviceProvider);
            await node.ProcessObjectAsync(dataContext, nodeContext);
            Assert.Equal(i + 2, dataContext.Get<int>("$.result"));
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(1, ExecuteCSharpNode.CompiledScriptCacheCount);
    }

    // The compiled-script cache is process-wide and keyed by the template text, so the
    // SAME script used by different pipelines/machines — which differ only in node
    // configuration such as rtIds, never in the script body — compiles once and is
    // shared. Two independent node instances running identical code must yield exactly
    // one cached compilation; this is what keeps the memory footprint flat as the number
    // of simulated machines grows.
    [Fact]
    public async Task ProcessObjectAsync_SameScriptDifferentNodes_SharesSingleCompilation()
    {
        ExecuteCSharpNode.ClearCompiledScriptCache();

        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "counter + 3 /* shared */",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "counter", ValuePath = "$.count", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };

        var node1 = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        var node2 = new ExecuteCSharpNode(A.Fake<NodeDelegate>());

        var (dc1, nc1) = PrepareTest(config, new JsonObject { ["count"] = 5 });
        await node1.ProcessObjectAsync(dc1, nc1);

        var (dc2, nc2) = PrepareTest(config, new JsonObject { ["count"] = 9 });
        await node2.ProcessObjectAsync(dc2, nc2);

        Assert.Equal(8, dc1.Get<int>("$.result"));
        Assert.Equal(12, dc2.Get<int>("$.result"));
        Assert.Equal(1, ExecuteCSharpNode.CompiledScriptCacheCount);
    }

    // An argument name is emitted as a bare C# identifier in the generated template, so a
    // name that is not a valid identifier must fail fast with a clear error rather than a
    // confusing Roslyn compilation error.
    [Theory]
    [InlineData("bad name")]
    [InlineData("x;y")]
    [InlineData("1abc")]
    [InlineData("a\"b")]
    public async Task ProcessObjectAsync_InvalidArgumentName_ThrowsClearError(string argName)
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "1",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = argName, Value = 1, DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var fn = A.Fake<NodeDelegate>();
        var node = new ExecuteCSharpNode(fn);

        var ex = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => node.ProcessObjectAsync(dataContext, nodeContext));
        Assert.Contains("not a valid C# identifier", ex.Message);
    }

    // Backward-compatibility guard for existing (adapter) scripts: a value-type argument
    // is declared NON-nullable, so its bare identifier is usable directly in a boolean
    // context — a comparison as a ternary/if condition (`count > 0 ? ...`). Declaring the
    // argument nullable (int?) would make `count > 0` a `bool?`, which cannot be a ternary
    // /if condition and would fail to compile HERE — silently breaking existing OEE scripts
    // that branch on a counter. This test pins the non-nullable declaration: do NOT "fix"
    // null handling by switching value-type arguments to nullable.
    [Fact]
    public async Task ProcessObjectAsync_ValueTypeArgumentInCondition_CompilesAndBranches()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "count > 0 ? \"producing\" : \"idle\"",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "count", ValuePath = "$.count", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.state"
        };
        var testData = new JsonObject { ["count"] = 7 };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("producing", dataContext.Get<string>("$.state"));
    }

    // Documents the deliberate behavior when a value-type argument's path is missing/null:
    // the non-nullable declaration coalesces it to the type's default (0 here), so the
    // script computes on the default rather than propagating null. This is the trade-off of
    // keeping bare identifiers usable in non-null contexts (see the condition test above);
    // it is pinned so any future change to null handling is a conscious edit, not drift.
    [Fact]
    public async Task ProcessObjectAsync_MissingValueTypeArgument_UsesTypeDefault()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "count + 1",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "count", ValuePath = "$.missing", DataType = AttributeValueTypesDto.Int }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(1, dataContext.Get<int>("$.result"));
    }

    // AB#5232 platform bug 2: array-typed arguments used to be declared as `object` and
    // arrived as a boxed JsonElement, so `foreach (var s in myStringArrayArg)` failed to
    // COMPILE ("does not contain a public instance definition for 'GetEnumerator'").
    // A StringArray argument must be a real string[] the script can enumerate directly.
    [Fact]
    public async Task ProcessObjectAsync_StringArrayArgument_ForeachCompilesAndRuns()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = @"
                var result = """";
                foreach (var s in names)
                {
                    result += s + ""|"";
                }
                return result;
            ",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "names", ValuePath = "$.names", DataType = AttributeValueTypesDto.StringArray }
            },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result"
        };
        var testData = new JsonObject { ["names"] = new JsonArray("alpha", "beta", "gamma") };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("alpha|beta|gamma|", dataContext.Get<string>("$.result"));
    }

    // AB#5232: a numeric array argument. The CK type system defines exactly three array
    // kinds (StringArray, IntArray/IntegerArray, RecordArray) — there is no DoubleArray —
    // so IntArray is the numeric array type; it materializes as int[] and supports LINQ.
    // The double RESULT of the aggregation checks that array elements feed numeric code.
    [Fact]
    public async Task ProcessObjectAsync_IntArrayArgument_SumAndAverageWork()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "values.Length == 0 ? 0.0 : values.Sum() / (double)values.Length",
            Usings = new List<string> { "System.Linq" },
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "values", ValuePath = "$.values", DataType = AttributeValueTypesDto.IntArray }
            },
            ReturnType = AttributeValueTypesDto.Double,
            TargetPath = "$.avg"
        };
        var testData = new JsonObject { ["values"] = new JsonArray(2, 4, 9) };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(5.0, dataContext.Get<double>("$.avg"));
    }

    // AB#5232: an empty JSON array materializes as an empty typed array — foreach runs
    // zero times, Length is 0, no null-reference and no cast error.
    [Fact]
    public async Task ProcessObjectAsync_EmptyArrayArgument_MaterializesEmptyTypedArray()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = @"
                var count = 0;
                foreach (var s in names) { count++; }
                return count + names.Length;
            ",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "names", ValuePath = "$.names", DataType = AttributeValueTypesDto.StringArray }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var testData = new JsonObject { ["names"] = new JsonArray() };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(0, dataContext.Get<int>("$.result"));
    }

    // AB#5232: null/absent behavior is UNCHANGED — a missing path resolves to null, which
    // coalesces to default(string[]) i.e. null, so scripts that null-check keep working.
    [Fact]
    public async Task ProcessObjectAsync_MissingArrayArgument_StaysNull()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "names == null ? \"NULL\" : \"NOT NULL\"",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "names", ValuePath = "$.missing", DataType = AttributeValueTypesDto.StringArray }
            },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("NULL", dataContext.Get<string>("$.result"));
    }

    // AB#5232: a fixed configuration Value (native list, the YamlDotNet shape) is
    // materialized into the typed array exactly like a path-resolved one.
    [Fact]
    public async Task ProcessObjectAsync_FixedValueArrayArgument_MaterializesTypedArray()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "string.Join(\",\", tags)",
            Arguments = new List<ScriptArgument>
            {
                new()
                {
                    Name = "tags",
                    Value = new List<object> { "a", "b", "c" },
                    DataType = AttributeValueTypesDto.StringArray
                }
            },
            ReturnType = AttributeValueTypesDto.String,
            TargetPath = "$.result"
        };
        var (dataContext, nodeContext) = PrepareTest(config);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("a,b,c", dataContext.Get<string>("$.result"));
    }

    // AB#5232 backward compatibility: a typed array IS an object, so scripts that treat
    // the argument generically (pass it around, ToString-free usage via Length etc.)
    // keep compiling and running.
    [Fact]
    public async Task ProcessObjectAsync_ArrayArgumentUsedAsObject_StillWorks()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = @"
                object boxed = names;
                return ((string[])boxed).Length;
            ",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "names", ValuePath = "$.names", DataType = AttributeValueTypesDto.StringArray }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var testData = new JsonObject { ["names"] = new JsonArray("x", "y") };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(2, dataContext.Get<int>("$.result"));
    }

    // AB#5232: RecordArray materializes as object[] — enumerable, with complex elements
    // preserved (JsonElement per record).
    [Fact]
    public async Task ProcessObjectAsync_RecordArrayArgument_IsEnumerableObjectArray()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = @"
                var count = 0;
                foreach (var r in records) { count++; }
                return count;
            ",
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "records", ValuePath = "$.records", DataType = AttributeValueTypesDto.RecordArray }
            },
            ReturnType = AttributeValueTypesDto.Int,
            TargetPath = "$.result"
        };
        var testData = new JsonObject
        {
            ["records"] = new JsonArray(
                new JsonObject { ["id"] = 1 },
                new JsonObject { ["id"] = 2 })
        };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(2, dataContext.Get<int>("$.result"));
    }

    // AB#5232: a script returning a lazy LINQ enumerable with ReturnType StringArray is
    // materialized to a proper JSON string array in the data context.
    [Fact]
    public async Task ProcessObjectAsync_StringArrayReturnType_MaterializesEnumerableResult()
    {
        var config = new ExecuteCSharpNodeConfiguration
        {
            Code = "names.Where(n => n.StartsWith(\"a\"))",
            Usings = new List<string> { "System.Linq" },
            Arguments = new List<ScriptArgument>
            {
                new() { Name = "names", ValuePath = "$.names", DataType = AttributeValueTypesDto.StringArray }
            },
            ReturnType = AttributeValueTypesDto.StringArray,
            TargetPath = "$.filtered"
        };
        var testData = new JsonObject { ["names"] = new JsonArray("apple", "banana", "avocado") };
        var (dataContext, nodeContext) = PrepareTest(config, testData);

        var node = new ExecuteCSharpNode(A.Fake<NodeDelegate>());
        await node.ProcessObjectAsync(dataContext, nodeContext);

        var filtered = dataContext.Get<string[]>("$.filtered");
        Assert.NotNull(filtered);
        Assert.Equal(new[] { "apple", "avocado" }, filtered);
    }
}
