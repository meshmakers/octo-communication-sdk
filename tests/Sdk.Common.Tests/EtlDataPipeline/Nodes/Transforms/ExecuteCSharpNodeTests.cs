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
    private (IDataContext, INodeContext) PrepareTest(ExecuteCSharpNodeConfiguration configuration, JsonObject? testData = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var data = testData ?? new JsonObject();
        var dataContext = new DataContextImpl(JsonDocument.Parse(data.ToJsonString()));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
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

        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            var (dataContext, nodeContext) = PrepareTest(config, new JsonObject { ["count"] = i });
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
}
