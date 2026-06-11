using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Reproduction of the STJ-migration regression where a <c>$.full</c> / <c>$.full.full</c>
/// reference (reaching the parent / grandparent context established by an enclosing
/// <c>ForEach</c>) is lost when the reference sits inside a <c>Switch</c> case body or a
/// <c>For</c> loop body.
///
/// Real-world failure (deployment energy-create-billing-items.yml):
///   Error in node 'PipelineExecution/ForEach@1':
///     No value found at ValuePath '$.full.full.Configuration.taxRate'
/// where the read sits inside a Switch case keyed on the metering point's type.
///
/// Root cause: <c>SwitchNode</c> and <c>ForNode</c> ran their child transformations in an
/// isolated sub-context built from <c>Select("$")</c> / <c>CreateSubContext</c>, which snapshots
/// the OVERLAY-ONLY <c>$</c> and drops the synthetic <c>$.full</c> aliases. <c>IfNode</c> and
/// <c>ForEachNode</c> preserve them (If runs on the same context; ForEach uses the alias-folding
/// iteration-child path), which is why the same <c>$.full.full</c> read works under If/ForEach
/// but throws under Switch/For.
///
/// On the OLD Newtonsoft pipeline this worked: the Switch/For body operated on the merged
/// document (<c>dataContext.Current</c>), which physically carried <c>full</c>, so the parent
/// chain stayed reachable.
/// </summary>
public class NestedSwitchForFullChainTests
{
    private const double RootTaxRate = 19.0;

    // Fresh fixture per test (NOT IClassFixture): each test mutates the node-lookup registry,
    // so a shared fixture would double-register a node type on the second test. The nested nodes
    // are resolved at runtime via INodeLookupService, so every node type used must be registered.
    private static (IDataContext dataContext, INodeContext nodeContext, ForEachNode testee) BuildOuterForEach(
        ForEachNodeConfiguration outerForEach, params Type[] nodeTypes)
    {
        var fixture = new NodeFixture();
        fixture.RegisterNode(typeof(ForEachNode));
        foreach (var t in nodeTypes)
        {
            fixture.RegisterNode(t);
        }

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(BuildRootJson()));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, outerForEach, dataContext);
        var testee = new ForEachNode(A.Fake<NodeDelegate>());
        return (dataContext, nodeContext, testee);
    }

    // Root context mirrors the production pipeline shape:
    //   $.Configuration.taxRate           -> the value the inner body must reach (root level)
    //   $.documents[*]                    -> outer iteration items (the "billing documents")
    //   $.documents[*].meteringPoints[*]  -> inner iteration items, each with a "kind" discriminator
    private static string BuildRootJson() => new JsonObject
    {
        ["Configuration"] = new JsonObject { ["taxRate"] = RootTaxRate },
        ["documents"] = new JsonArray(
            new JsonObject
            {
                ["id"] = 1,
                ["meteringPoints"] = new JsonArray(
                    new JsonObject { ["mp"] = "A", ["kind"] = "tax" },
                    new JsonObject { ["mp"] = "B", ["kind"] = "tax" })
            },
            new JsonObject
            {
                ["id"] = 2,
                ["meteringPoints"] = new JsonArray(
                    new JsonObject { ["mp"] = "C", ["kind"] = "tax" })
            })
    }.ToJsonString();

    /// <summary>
    /// ForEach(documents) &gt; ForEach(meteringPoints) &gt; Switch(kind) &gt; case "tax" &gt;
    /// SetPrimitiveValue copying <c>$.full.full.Configuration.taxRate</c> (the ROOT taxRate) into
    /// the inner item. Every inner item must receive copied == 19.0.
    ///
    /// Before the fix this threw
    ///   PipelineExecutionException: No value found at ValuePath '$.full.full.Configuration.taxRate'
    /// because the Switch case body's isolated sub-context dropped the inner "$.full" alias.
    /// </summary>
    [Fact]
    public async Task NestedForEachSwitch_GrandparentFull_ResolvesRootConfig()
    {
        var setTaxRate = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.key.copied",
            ValuePath = "$.full.full.Configuration.taxRate",
            ValueType = AttributeValueTypesDto.Double
        };
        var switchNode = new SwitchNodeConfiguration
        {
            Path = "$.key.kind",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new() { Value = "tax", Transformations = new List<NodeConfiguration> { setTaxRate } }
            }
        };
        var innerForEach = new ForEachNodeConfiguration
        {
            Path = "$",
            IterationPath = "$.key.meteringPoints",
            TargetPath = "$.key.innerResult",
            MergePath = "$.key",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration> { switchNode }
        };
        var outerForEach = new ForEachNodeConfiguration
        {
            Path = "$",
            IterationPath = "$.documents",
            TargetPath = "$.Result",
            MergePath = "$.key",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration> { innerForEach }
        };

        var (dataContext, nodeContext, testee) = BuildOuterForEach(
            outerForEach, typeof(SwitchNode), typeof(SetPrimitiveValueNode));

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(2, dataContext.Length("$.Result"));
        var copiedValues = new List<double>();
        for (var i = 0; i < dataContext.Length("$.Result"); i++)
        {
            var innerCount = dataContext.Length($"$.Result[{i}].innerResult");
            for (var j = 0; j < innerCount; j++)
            {
                copiedValues.Add(dataContext.Get<double>($"$.Result[{i}].innerResult[{j}].copied"));
            }
        }

        Assert.Equal(3, copiedValues.Count);
        Assert.All(copiedValues, v => Assert.Equal(RootTaxRate, v));
    }

    /// <summary>
    /// A static write inside a Switch case body must persist into the iteration result (the
    /// case body operates on the live context, mirroring the old merged-document write-back).
    /// </summary>
    [Fact]
    public async Task NestedForEachSwitch_StaticWriteInCaseBody_PersistsToResult()
    {
        var setMarker = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.key.marker",
            Value = 7,
            ValueType = AttributeValueTypesDto.Int
        };
        var switchNode = new SwitchNodeConfiguration
        {
            Path = "$.key.kind",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new() { Value = "tax", Transformations = new List<NodeConfiguration> { setMarker } }
            }
        };
        var innerForEach = new ForEachNodeConfiguration
        {
            Path = "$",
            IterationPath = "$.key.meteringPoints",
            TargetPath = "$.key.innerResult",
            MergePath = "$.key",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration> { switchNode }
        };
        var outerForEach = new ForEachNodeConfiguration
        {
            Path = "$",
            IterationPath = "$.documents",
            TargetPath = "$.Result",
            MergePath = "$.key",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration> { innerForEach }
        };

        var (dataContext, nodeContext, testee) = BuildOuterForEach(
            outerForEach, typeof(SwitchNode), typeof(SetPrimitiveValueNode));

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var markers = new List<int>();
        for (var i = 0; i < dataContext.Length("$.Result"); i++)
        {
            var innerCount = dataContext.Length($"$.Result[{i}].innerResult");
            for (var j = 0; j < innerCount; j++)
            {
                markers.Add(dataContext.Get<int>($"$.Result[{i}].innerResult[{j}].marker"));
            }
        }

        Assert.Equal(3, markers.Count);
        Assert.All(markers, m => Assert.Equal(7, m));
    }

    /// <summary>
    /// ForEach(documents) &gt; For(count=2) &gt; SetPrimitiveValue copying
    /// <c>$.full.Configuration.taxRate</c> (the ROOT taxRate, one level up from inside the For
    /// body — the For runs inside the ForEach child whose <c>$.full</c> is the root) into each
    /// For iteration. The For target collects one entry per iteration, each carrying copied == 19.0.
    ///
    /// Before the fix this threw
    ///   PipelineExecutionException: No value found at ValuePath '$.full.Configuration.taxRate'
    /// because the For body's isolated sub-context dropped the "$.full" alias.
    /// </summary>
    [Fact]
    public async Task NestedForEachFor_ParentFull_ResolvesRootConfig()
    {
        var setTaxRate = new SetPrimitiveValueNodeConfiguration
        {
            TargetPath = "$.copied",
            ValuePath = "$.full.Configuration.taxRate",
            ValueType = AttributeValueTypesDto.Double
        };
        var forNode = new ForNodeConfiguration
        {
            Path = "$",
            Count = 2,
            TargetPath = "$.key.forResult",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration> { setTaxRate }
        };
        var outerForEach = new ForEachNodeConfiguration
        {
            Path = "$",
            IterationPath = "$.documents",
            TargetPath = "$.Result",
            MergePath = "$.key",
            MaxDegreeOfParallelism = 1,
            Transformations = new List<NodeConfiguration> { forNode }
        };

        var (dataContext, nodeContext, testee) = BuildOuterForEach(
            outerForEach, typeof(ForNode), typeof(SetPrimitiveValueNode));

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(2, dataContext.Length("$.Result"));
        var copiedValues = new List<double>();
        for (var i = 0; i < dataContext.Length("$.Result"); i++)
        {
            var forCount = dataContext.Length($"$.Result[{i}].forResult");
            for (var j = 0; j < forCount; j++)
            {
                copiedValues.Add(dataContext.Get<double>($"$.Result[{i}].forResult[{j}].copied"));
            }
        }

        // 2 documents x 2 For iterations = 4 copied values, all == root taxRate.
        Assert.Equal(4, copiedValues.Count);
        Assert.All(copiedValues, v => Assert.Equal(RootTaxRate, v));
    }
}
