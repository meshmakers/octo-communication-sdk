using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

/// <summary>
/// A Group must be transparent to the data context: a ForEach wrapped in a Group resolves
/// "$.full" (one level up to the root) exactly as it would inline. If the Group accidentally
/// created an isolated sub-context/overlay, the inner "$.full.Configuration.taxRate" read would
/// fail. Guards the same-context execution contract.
/// </summary>
public class GroupNodeFullChainTests
{
    private const double RootTaxRate = 19.0;

    // A fresh fixture per test (NOT IClassFixture): each RegisterNode call mutates the node-lookup
    // registry; a shared fixture would double-register "ForEach@1" on a second test.
    private static NodeFixture CreateFixture()
    {
        var fixture = new NodeFixture();
        fixture.RegisterNode(typeof(ForEachNode));
        fixture.RegisterNode(typeof(SetPrimitiveValueNode));
        return fixture;
    }

    private static string BuildRootJson() => new JsonObject
    {
        ["Configuration"] = new JsonObject { ["taxRate"] = RootTaxRate },
        ["items"] = new JsonArray(
            new JsonObject { ["id"] = 1 },
            new JsonObject { ["id"] = 2 })
    }.ToJsonString();

    [Fact]
    public async Task GroupWrappingForEach_ResolvesParentFullAlias()
    {
        var config = new GroupNodeConfiguration
        {
            Name = "wrapper",
            Transformations = new List<NodeConfiguration>
            {
                new ForEachNodeConfiguration
                {
                    Path = "$",
                    IterationPath = "$.items",
                    TargetPath = "$.Result",
                    MergePath = "$.key",
                    MaxDegreeOfParallelism = 1,
                    Transformations = new List<NodeConfiguration>
                    {
                        new SetPrimitiveValueNodeConfiguration
                        {
                            TargetPath = "$.key.copied",
                            ValuePath = "$.full.Configuration.taxRate",
                            ValueType = AttributeValueTypesDto.Double
                        }
                    }
                }
            }
        };

        var fixture = CreateFixture();
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(BuildRootJson()));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Group", 0, config, dataContext);
        var testee = new GroupNode(A.Fake<NodeDelegate>());

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(2, dataContext.Length("$.Result"));
        Assert.Equal(RootTaxRate, dataContext.Get<double>("$.Result[0].copied"));
        Assert.Equal(RootTaxRate, dataContext.Get<double>("$.Result[1].copied"));
    }
}
