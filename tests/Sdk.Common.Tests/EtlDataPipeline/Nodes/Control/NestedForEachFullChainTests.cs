using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

/// <summary>
/// Synthetic reproduction of a ForEach-inside-a-ForEach (nested iteration) reading the
/// ROOT context two levels up via "$.full.full.X".
///
/// Real-world failure (deployment energy-create-billing-items.yml):
///   Error in node 'PipelineExecution/ForEach@1':
///     No value found at ValuePath '$.full.full.Configuration.taxRate'
/// where $.Configuration.taxRate is set on the ROOT context, the OUTER ForEach iterates the
/// documents, and the INNER ForEach (over the document's metering points) reads the root tax
/// rate as "$.full.full.Configuration.taxRate".
///
/// DataContext "$.full" semantics (per pipeline-expert + ForEachNode):
///   inner $.key           -> current inner item
///   inner $.full.key      -> the OUTER item   (1 level up)
///   inner $.full.full.X   -> the ROOT context (2 levels up)
///
/// Root cause (STJ migration, branch dev/reimar/stj-pipeline-migration):
///   ForEachNode builds each child's "$.full" by deep-serialising the parent's "$" VIEW
///   (DataContextImpl.ResolveAliasElements -> GetAsNode("$")). After the outer ForEach does
///   Set("$.key", item) the outer child's overlay is lifted to { "key": <outerItem> } and a
///   read of "$" returns ONLY that overlay — the outer child's own "$.full" lives in a
///   separate alias map and is excluded from a "$" read (CanonicalPath.IsAncestor("$.full","$")
///   is false). So the inner "$.full" snapshot has no "full" key, "$.full.full" resolves to
///   DataKind.Undefined, and SetPrimitiveValueNode throws "No value found at ValuePath ...".
///
///   ONE level ("$.full.key.X") still works via the alias snapshot; only the grandparent
///   chain ("$.full.full.X") is broken. The two tests below differ ONLY in that depth.
///
/// On the OLD Newtonsoft pipeline this worked: the outer child was a single merged document
/// { full: <root>, key: <outerItem> }, so the inner "$.full" copy contained "full" and
/// "$.full.full" reached the root.
/// </summary>
public class NestedForEachFullChainTests
{
    private const double RootTaxRate = 19.0;

    // A fresh fixture per test (NOT IClassFixture): each test mutates the node-lookup registry,
    // so a shared fixture would double-register "ForEach@1" on the second test and throw at
    // dictionary build time. The nested ForEach and the inner SetPrimitiveValue are resolved at
    // runtime via the INodeLookupService, so both node types must be registered (the fixture
    // seeds only the Test* nodes by default).
    private static NodeFixture CreateFixture()
    {
        var fixture = new NodeFixture();
        fixture.RegisterNode(typeof(ForEachNode));
        fixture.RegisterNode(typeof(SetPrimitiveValueNode));
        return fixture;
    }

    // Root context mirrors the production pipeline shape with pure synthetic data:
    //   $.Configuration.taxRate           -> the value the inner loop must reach (root level)
    //   $.documents[*]                    -> outer iteration items (the "billing documents")
    //   $.documents[*].meteringPoints[*]  -> inner iteration items
    private static string BuildRootJson() => new JsonObject
    {
        ["Configuration"] = new JsonObject { ["taxRate"] = RootTaxRate },
        ["documents"] = new JsonArray(
            new JsonObject
            {
                ["id"] = 1,
                ["meteringPoints"] = new JsonArray(
                    new JsonObject { ["mp"] = "A" },
                    new JsonObject { ["mp"] = "B" })
            },
            new JsonObject
            {
                ["id"] = 2,
                ["meteringPoints"] = new JsonArray(
                    new JsonObject { ["mp"] = "C" })
            })
    }.ToJsonString();

    private static (IDataContext dataContext, INodeContext nodeContext, ForEachNode testee) BuildOuterForEach(
        ForEachNodeConfiguration outerForEach)
    {
        var fixture = CreateFixture();
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(BuildRootJson()));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0, outerForEach, dataContext);
        var testee = new ForEachNode(A.Fake<NodeDelegate>());
        return (dataContext, nodeContext, testee);
    }

    /// <summary>
    /// Builds the outer ForEach (over $.documents) whose only child is an inner ForEach
    /// (over $.key.meteringPoints) whose only child is a SetPrimitiveValue that copies
    /// <paramref name="innerValuePath"/> into the inner item.
    /// </summary>
    private static ForEachNodeConfiguration BuildNestedConfig(string innerValuePath) => new()
    {
        Path = "$",
        IterationPath = "$.documents",
        TargetPath = "$.Result",
        MergePath = "$.key",
        MaxDegreeOfParallelism = 1,
        Transformations = new List<NodeConfiguration>
        {
            new ForEachNodeConfiguration
            {
                Path = "$",
                IterationPath = "$.key.meteringPoints",
                TargetPath = "$.key.innerResult",
                MergePath = "$.key",
                MaxDegreeOfParallelism = 1,
                Transformations = new List<NodeConfiguration>
                {
                    new SetPrimitiveValueNodeConfiguration
                    {
                        TargetPath = "$.key.copied",
                        ValuePath = innerValuePath,
                        ValueType = AttributeValueTypesDto.Double
                    }
                }
            }
        }
    };

    /// <summary>
    /// CONTROL: reading ONE level up ("$.full.key.id" = the current outer document's id) inside a
    /// nested ForEach resolves correctly and the pipeline completes. This proves the nested-ForEach
    /// machinery itself works and isolates the failure below to the grandparent ("$.full.full") chain.
    /// </summary>
    [Fact]
    public async Task NestedForEach_SingleLevelFull_ResolvesOuterItem()
    {
        var (dataContext, nodeContext, testee) = BuildOuterForEach(BuildNestedConfig("$.full.key.id"));

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Two outer documents iterated; pipeline completed without throwing.
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.Result"));
        Assert.Equal(2, dataContext.Length("$.Result"));
    }

    /// <summary>
    /// Reading TWO levels up ("$.full.full.Configuration.taxRate" = the ROOT taxRate) inside a nested
    /// ForEach must resolve to the root Configuration value, exactly as the old Newtonsoft pipeline did.
    /// Every inner item (metering point) of every outer item (document) must receive copied == 19.0.
    ///
    /// Before the grandparent-chain fix this threw
    ///   PipelineExecutionException: No value found at ValuePath '$.full.full.Configuration.taxRate'
    /// (the inner "$.full" snapshot dropped the outer "$.full" alias).
    /// </summary>
    [Fact]
    public async Task NestedForEach_GrandparentFull_ResolvesRootConfig()
    {
        var (dataContext, nodeContext, testee) =
            BuildOuterForEach(BuildNestedConfig("$.full.full.Configuration.taxRate"));

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Two outer documents (ids 1 and 2) with 2 + 1 metering points = 3 inner items total.
        Assert.Equal(2, dataContext.Length("$.Result"));

        var copiedValues = new List<double>();
        var documentCount = dataContext.Length("$.Result");
        for (var i = 0; i < documentCount; i++)
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
    /// Debug-capture analogue of <see cref="NestedForEach_GrandparentFull_ResolvesRootConfig"/>:
    /// the pipeline debugger must expose the SAME grandparent alias chain that the read path
    /// resolves. A node inside the INNER ForEach must capture "$.full.full" (the ROOT context) in
    /// its snapshot, so the refinery-studio debug views show it — exactly as the old Newtonsoft
    /// pipeline did. Guards that the debug snapshot folds aliases transitively, not just one level.
    /// </summary>
    [Fact]
    public async Task NestedForEach_GrandparentFull_AppearsInDebugSnapshot()
    {
        var fixture = CreateFixture();
        var serviceProvider = fixture.Services.BuildServiceProvider();
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());
        debugger.RegisterPipelineRtEntityId(
            new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId()), Guid.NewGuid());

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(BuildRootJson()));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(serviceProvider, logger, dataContext, debugger);
        var nodeContext = rootNodeContext.RegisterChildNode("ForEach", 0,
            BuildNestedConfig("$.full.full.Configuration.taxRate"), dataContext);
        var testee = new ForEachNode(A.Fake<NodeDelegate>());

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Across all captured snapshots, at least one inner-loop node must carry the full
        // grandparent chain: $.full.full.Configuration.taxRate == the ROOT taxRate.
        var snapshots = debugger.GetDebugInformation().DebugPoints
            .SelectMany(dp => new[] { dp.Input, dp.Output })
            .Where(s => s is not null)
            .Select(s => JsonNode.Parse(s!)!.AsObject())
            .ToList();

        static bool HasGrandparentTaxRate(JsonObject snapshot)
        {
            var taxRate = snapshot["full"]?["full"]?["Configuration"]?["taxRate"];
            return taxRate is not null && taxRate.GetValue<double>() == RootTaxRate;
        }

        Assert.Contains(snapshots, HasGrandparentTaxRate);
    }
}
