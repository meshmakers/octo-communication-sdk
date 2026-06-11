using System.Linq;
using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

public class GroupNodeTests
{
    // Fresh fixture per test: GroupNode is resolved at runtime when a group nests another group,
    // so it must be registered; a shared IClassFixture would double-register on the second test.
    private static NodeFixture CreateFixture()
    {
        var fixture = new NodeFixture();
        fixture.RegisterNode(typeof(GroupNode));
        return fixture;
    }

    private static (IDataContext, INodeContext) PrepareTest(
        NodeFixture fixture, GroupNodeConfiguration config, object? testData = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = testData ?? new { X = "keep" };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Group", 0, config, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_RunsChildrenOnSameContext_AndForwards()
    {
        var config = new GroupNodeConfiguration
        {
            Name = "set fields",
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.A" },
                new TestNodeConfiguration { TargetPath = "$.B" }
            }
        };

        var fixture = CreateFixture();
        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).ReturnsNextFromSequence(1, 2);

        var (dataContext, nodeContext) = PrepareTest(fixture, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new GroupNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(1, dataContext.Get<int>("$.A"));
        Assert.Equal(2, dataContext.Get<int>("$.B"));
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_AddsNothingBeyondChildren_NoOpParity()
    {
        // Grouping must equal inlining: the group itself introduces no extra keys/mutations.
        var config = new GroupNodeConfiguration
        {
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.A" },
                new TestNodeConfiguration { TargetPath = "$.B" }
            }
        };

        var fixture = CreateFixture();
        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).ReturnsNextFromSequence(1, 2);

        var (dataContext, nodeContext) = PrepareTest(fixture, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new GroupNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        // Seed key preserved, exactly the two child writes added, nothing else.
        Assert.Equal("keep", dataContext.Get<string>("$.X"));
        Assert.Equal(1, dataContext.Get<int>("$.A"));
        Assert.Equal(2, dataContext.Get<int>("$.B"));
        Assert.Equal(3, dataContext.Keys("$").Count()); // X, A, B — the Group added no key of its own
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyGroup_IsPassThrough()
    {
        var config = new GroupNodeConfiguration { Transformations = new List<NodeConfiguration>() };

        var fixture = CreateFixture();
        var (dataContext, nodeContext) = PrepareTest(fixture, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new GroupNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("keep", dataContext.Get<string>("$.X"));
        Assert.Single(dataContext.Keys("$")); // unchanged — only X remains
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_NestedGroups_RunInnerChildren()
    {
        var config = new GroupNodeConfiguration
        {
            Name = "outer",
            Transformations = new List<NodeConfiguration>
            {
                new GroupNodeConfiguration
                {
                    Name = "inner",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.A" }
                    }
                }
            }
        };

        var fixture = CreateFixture();
        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(7);

        var (dataContext, nodeContext) = PrepareTest(fixture, config);
        var fn = A.Fake<NodeDelegate>();
        var testee = new GroupNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal(7, dataContext.Get<int>("$.A"));
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }
}
