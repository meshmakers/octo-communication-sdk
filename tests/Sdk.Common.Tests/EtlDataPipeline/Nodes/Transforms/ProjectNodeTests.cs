using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class ProjectNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(ProjectNodeConfiguration projectNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var data = Generator.GenerateColumnDataNode();
        var dataContext = new DataContextImpl(JsonDocument.Parse(data.ToJsonString()));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Project", 0, projectNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    private static IReadOnlyList<string> KeysAt(IDataContext ctx, string path) => ctx.Keys(path).ToList();

    [Fact]
    public async Task ProcessObjectAsync_ExcludeByField_OK()
    {
        ProjectNodeConfiguration projectNodeConfiguration = new()
        {
            Fields = new List<FieldConfiguration>
            {
                new()
                {
                    Path = "$.data.timestamp"
                },
                new()
                {
                    Path = "$.data.batteryPower"
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(projectNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ProjectNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var keys = KeysAt(dataContext, "$.data");
        Assert.Equal(5, keys.Count);
        Assert.Contains("productionPower", keys);
        Assert.DoesNotContain("timestamp", keys);
        Assert.DoesNotContain("batteryPower", keys);
        Assert.False(dataContext.Exists("$.data.timestamp"));
        Assert.False(dataContext.Exists("$.data.batteryPower"));
    }

    [Fact]
    public async Task ProcessObjectAsync_UsePathAndExcludeByField_OK()
    {
        ProjectNodeConfiguration projectNodeConfiguration = new()
        {
            Path = "$.data",
            Fields = new List<FieldConfiguration>
            {
                new()
                {
                    Path = "$.timestamp"
                },
                new()
                {
                    Path = "$.batteryPower"
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(projectNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ProjectNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var keys = KeysAt(dataContext, "$.data");
        Assert.Equal(5, keys.Count);
        Assert.Contains("productionPower", keys);
        Assert.DoesNotContain("timestamp", keys);
        Assert.DoesNotContain("batteryPower", keys);
    }

    [Fact]
    public async Task ProcessObjectAsync_ClearAndWhitelist_OK()
    {
        ProjectNodeConfiguration projectNodeConfiguration = new()
        {
            Clear = true,
            Fields = new List<FieldConfiguration>
            {
                new()
                {
                    Path = "$.data.timestamp",
                    Inclusion = true
                },
                new()
                {
                    Path = "$.data.batteryPower",
                    Inclusion = true
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(projectNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ProjectNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var keys = KeysAt(dataContext, "$.data");
        Assert.Equal(2, keys.Count);
        Assert.DoesNotContain("productionPower", keys);
        Assert.Contains("timestamp", keys);
        Assert.Contains("batteryPower", keys);
    }

    [Fact]
    public async Task ProcessObjectAsync_UsePathClearAndWhitelist_OK()
    {
        ProjectNodeConfiguration projectNodeConfiguration = new()
        {
            Path = "$.data",
            Clear = true,
            Fields = new List<FieldConfiguration>
            {
                new()
                {
                    Path = "$.timestamp",
                    Inclusion = true
                },
                new()
                {
                    Path = "$.batteryPower",
                    Inclusion = true
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(projectNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ProjectNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var keys = KeysAt(dataContext, "$.data");
        Assert.Equal(2, keys.Count);
        Assert.DoesNotContain("productionPower", keys);
        Assert.Contains("timestamp", keys);
        Assert.Contains("batteryPower", keys);
    }

    [Fact]
    public async Task ProjectNode_WalkerCollapse_RetainsExistingDottedSemantics()
    {
        // Phase 2.5.2: SelectByPath / RemoveByPath / SetNestedValue / ParsePath are gone;
        // ProjectNode now routes Field path resolution through JsonNodePath. There is no
        // meaningful capability-gain test we can craft here because the same Field path
        // drives both the read (snapshot lookup, full dialect available) AND the write
        // (Set/Remove on freshObject/working, dotted-only by design). Bracket / wildcard /
        // filter forms therefore can't survive the round-trip in this node.
        //
        // Phase 2.3 (commit 32cffd3) already covers multi-match c.Path support via
        // ProjectNode_MultiMatchPath_ProjectsEachMatchInPlace below. This test simply
        // pins the dotted semantics that the new walker must continue to honour: a deeply
        // nested dotted Inclusion path is read from the snapshot and written into the
        // fresh output unchanged.
        ProjectNodeConfiguration projectNodeConfiguration = new()
        {
            Clear = true,
            Fields = new List<FieldConfiguration>
            {
                new()
                {
                    Path = "$.data.timestamp",
                    Inclusion = true
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(projectNodeConfiguration);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ProjectNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var keys = KeysAt(dataContext, "$.data");
        Assert.Single(keys);
        Assert.Contains("timestamp", keys);
    }

    [Fact]
    public async Task ProjectNode_MultiMatchPath_ProjectsEachMatchInPlace()
    {
        // Multi-match Path: every items[i] should be projected (b removed) — not just the last.
        // Pre-fix: only the last match's projected node was written, collapsing all matches to
        // a single literal "$.items[*]" Set call (last-wins). After fix, each match is updated
        // in place via UpdateMatchesAsync.
        ProjectNodeConfiguration projectNodeConfiguration = new()
        {
            Path = "$.items[*]",
            Fields = new List<FieldConfiguration>
            {
                new()
                {
                    Path = "$.b",
                    Inclusion = false
                }
            }
        };

        var logger = A.Fake<IPipelineLogger>();
        var doc = JsonDocument.Parse("""{"items":[{"a":1,"b":2},{"a":3,"b":4}]}""");
        var dataContext = new DataContextImpl(doc);
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Project", 0, projectNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new ProjectNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        // Each match has b removed and a preserved.
        Assert.Equal(2, dataContext.Length("$.items"));

        var item0Keys = dataContext.Keys("$.items[0]").ToList();
        Assert.Single(item0Keys);
        Assert.Contains("a", item0Keys);
        Assert.Equal(1, dataContext.Get<int>("$.items[0].a"));

        var item1Keys = dataContext.Keys("$.items[1]").ToList();
        Assert.Single(item1Keys);
        Assert.Contains("a", item1Keys);
        Assert.Equal(3, dataContext.Get<int>("$.items[1].a"));
    }
}
