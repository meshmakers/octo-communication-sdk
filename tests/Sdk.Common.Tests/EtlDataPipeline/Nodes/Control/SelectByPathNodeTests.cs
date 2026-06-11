using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

public class SelectByPathNodeTests(NodeFixture fixture)
    : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext, Order) PrepareTest(SelectByPathNodeConfiguration selectByPathNodeConfiguration)
    {
        var order = Generator.GenerateOrder();
        var logger = A.Fake<IPipelineLogger>();
        var json = JsonSerializer.Serialize(order, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SelectByPath", 0, selectByPathNodeConfiguration, dataContext);
        return (dataContext, nodeContext, order);
    }

    [Fact]
    public async Task ProcessObjectAsync_Object_String_NoTransforms_OK()
    {
        SelectByPathNodeConfiguration selectByPathNodeConfiguration = new()
        {
            SelectPath = new List<PathPropertyConfigurationNode>
            {
                new()
                {
                    Path = "$.Customer.Name",
                    TargetPath = "$.CustomerName"
                }
            }
        };

        var (dataContext, nodeContext, order) = PrepareTest(selectByPathNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SelectByPathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(order.Customer.Name, dataContext.Get<string>("$.CustomerName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Object_String_WithTransforms_OK()
    {
        SelectByPathNodeConfiguration selectByPathNodeConfiguration = new()
        {
            SelectPath = new List<PathPropertyConfigurationNode>
            {
                new()
                {
                    Path = "$.Customer.Name",
                    TargetPath = "$.TestProperty",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration()
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(567);

        var (dataContext, nodeContext, _) = PrepareTest(selectByPathNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SelectByPathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(1, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(567, dataContext.Get<int>("$.TestProperty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Object_Int32_NoTransforms_OK()
    {
        SelectByPathNodeConfiguration selectByPathNodeConfiguration = new()
        {
            SelectPath = new List<PathPropertyConfigurationNode>
            {
                new()
                {
                    Path = "$.Customer.Id",
                    TargetPath = "$.CustomerId"
                }
            }
        };

        var (dataContext, nodeContext, order) = PrepareTest(selectByPathNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SelectByPathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(order.Customer.Id, dataContext.Get<int>("$.CustomerId"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Object_Int32_WithTransforms_OK()
    {
        SelectByPathNodeConfiguration selectByPathNodeConfiguration = new()
        {
            SelectPath = new List<PathPropertyConfigurationNode>
            {
                new()
                {
                    Path = "$.Customer.Id",
                    TargetPath = "$.TestProperty",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration()
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(987);

        var (dataContext, nodeContext, _) = PrepareTest(selectByPathNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SelectByPathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(1, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(987, dataContext.Get<int>("$.TestProperty"));
    }

    [Fact]
    public async Task SelectByPathNode_MultiMatchPath_TakesFirstMatchOnly()
    {
        // Pre-migration SelectByPathNode used Newtonsoft's SelectToken (singular) which
        // always returned the FIRST match. Post-migration code switched to EnumerateMatches
        // and ran per-match body invocations whose outputs all collided on sel.TargetPath
        // via last-write-wins. Lock the first-match parity invariant: a multi-match Path
        // must invoke the body exactly ONCE (for the first match), not once per match.
        SelectByPathNodeConfiguration selectByPathNodeConfiguration = new()
        {
            SelectPath = new List<PathPropertyConfigurationNode>
            {
                new()
                {
                    Path = "$.items[*].v",
                    TargetPath = "$.firstValue",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration()
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var logger = A.Fake<IPipelineLogger>();
        var doc = JsonDocument.Parse("""{"items":[{"v":1},{"v":2},{"v":3}]}""");
        var dataContext = new DataContextImpl(doc);
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SelectByPath", 0, selectByPathNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SelectByPathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Body must be invoked exactly once — for the first match — not once per match.
        // Pre-fix: the body ran 3 times (once per match), and the 3 results collided at
        // sel.TargetPath via last-write-wins. This deterministic assertion catches that
        // regardless of any ordering non-determinism in the legacy ConcurrentBag flush.
        A.CallTo(() => testCounter.GetNext()).MustHaveHappened(1, Times.Exactly);
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }
}
