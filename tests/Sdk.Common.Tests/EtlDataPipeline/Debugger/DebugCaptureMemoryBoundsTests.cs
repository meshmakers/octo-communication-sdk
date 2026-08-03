using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.Debugger;

/// <summary>
/// Guards the AB#4662 memory bounds of debug-mode capture: per-iteration snapshots must not
/// carry the alias-folded parent document, total snapshot retention must be budgeted, and the
/// debug message queue must be capped — with debugging enabled, a large nested-ForEach run
/// previously grew all three without bound and OOM-killed the adapter.
/// </summary>
public class DebugCaptureMemoryBoundsTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    private static DefaultPipelineDebugger NewDebugger()
    {
        var debugger = new DefaultPipelineDebugger(NullLoggerFactory.Instance);
        debugger.RegisterPipelineRtEntityId(
            new RtEntityId("Test/Pipeline", OctoObjectId.GenerateNewId()), Guid.NewGuid());
        return debugger;
    }

    [Fact]
    public void IterationChild_DebugSnapshot_ReplacesAliasValueWithPlaceholder()
    {
        var root = new DataContextImpl(Doc("""{"items":[{"id":1},{"id":2}],"big":"payload"}"""));
        var child = root.CreateIterationChild(
            new[] { ("$.full", Doc("""{"items":[{"id":1},{"id":2}],"big":"payload"}""")) });
        child.Set("$.key", JsonNode.Parse("""{"id":1}"""));

        var snapshot = ((IDebugSnapshotSource)child).GetDebugSnapshot();

        var obj = Assert.IsAssignableFrom<JsonObject>(snapshot);
        Assert.True(JsonNode.DeepEquals(obj["key"], JsonNode.Parse("""{"id":1}""")),
            $"Iteration item must stay in the snapshot. Snapshot: {obj.ToJsonString()}");
        var full = obj["full"];
        Assert.NotNull(full);
        Assert.Contains("omitted from debug snapshot", full!.GetValue<string>());
    }

    [Fact]
    public void IterationChild_ExecutionReads_StillResolveAliasValues()
    {
        // The placeholder is a DEBUG-capture-only substitution: execution reads (alias
        // resolution for nested iteration via GetEffectiveNode) must keep folding real values,
        // so a grandchild still reaches the grandparent document through "$.full.full".
        var root = new DataContextImpl(Doc("""{"a":1}"""));
        // Lift the overlay — a real pipeline root always has writes (trigger/extract output);
        // an unlifted pure-element root resolves "$" to null in ResolveAliasElements.
        root.Set("$.b", 2);
        // Children carry an iteration item like a real ForEach child does ($.key/root item) —
        // alias folding in GetEffectiveNode builds on the child's written "$" view.
        var l1Aliases = ((IIterationContextFactory)root).ResolveAliasElements(new[] { ("$.full", "$") });
        var l1 = ((IIterationContextFactory)root).CreateIterationChild(l1Aliases, JsonNode.Parse("""{"item":1}"""));
        var l2Aliases = ((IIterationContextFactory)l1).ResolveAliasElements(new[] { ("$.full", "$") });
        var l2 = ((IIterationContextFactory)l1).CreateIterationChild(l2Aliases, JsonNode.Parse("""{"item":2}"""));

        Assert.Equal(1, l1.Get<int>("$.full.a"));
        Assert.Equal(1, l2.Get<int>("$.full.full.a"));
    }

    [Fact]
    public void SnapshotCapture_ExhaustedTotalBudget_StoresPlaceholder()
    {
        var debugger = NewDebugger();
        debugger.MaxTotalRetainedSnapshotChars = 100;

        var payload = JsonNode.Parse($$"""{"data":"{{new string('x', 150)}}"}""")!;
        debugger.LogOutput("0:a", new NodePath("a"), null, 0, payload);
        debugger.LogOutput("1:b", new NodePath("b"), null, 1, payload);

        var points = debugger.GetDebugInformation().DebugPoints;
        var first = points.Single(p => p.NodeId == "0:a");
        var second = points.Single(p => p.NodeId == "1:b");
        // The first capture fits (budget checked before charging); the second finds the budget
        // exhausted and degrades to the placeholder instead of retaining another full payload.
        Assert.StartsWith("{\"data\":", first.Output);
        Assert.StartsWith("<debug snapshot omitted: total debug capture budget exhausted", second.Output);
    }

    [Fact]
    public void SnapshotCapture_RecaptureSameNode_DoesNotDoubleCharge()
    {
        var debugger = NewDebugger();
        var payload = JsonNode.Parse($$"""{"data":"{{new string('x', 100)}}"}""")!;
        var payloadChars = payload.ToJsonString().Length;
        debugger.MaxTotalRetainedSnapshotChars = payloadChars + 10;

        // Same node id captured twice: the replacement must credit the replaced string back,
        // so the budget still has room for a different node afterwards.
        debugger.LogOutput("0:a", new NodePath("a"), null, 0, payload);
        debugger.LogOutput("0:a", new NodePath("a"), null, 0, payload);
        debugger.LogOutput("1:b", new NodePath("b"), null, 1, JsonNode.Parse("""{"n":1}"""));

        var second = debugger.GetDebugInformation().DebugPoints.Single(p => p.NodeId == "1:b");
        Assert.Equal("""{"n":1}""", second.Output);
    }

    [Fact]
    public void DebugMessages_AreCappedAtMaxRetained()
    {
        var logger = new DebugPipelineLogger(NullLoggerFactory.Instance) { MaxRetainedMessages = 3 };

        for (var i = 0; i < 5; i++)
        {
            logger.Debug("node", "path", $"message {i}");
        }

        Assert.Equal(3, logger.Messages.Count);
        Assert.Equal(2, logger.DroppedMessageCount);

        logger.Clear();
        logger.Debug("node", "path", "after clear");
        Assert.Single(logger.Messages);
        Assert.Equal(0, logger.DroppedMessageCount);
    }
}
