using System;
using System.Linq;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.Debugger;

/// <summary>
/// Behaviour guard for the pipeline debugger's per-node snapshot: the snapshot string must
/// faithfully preserve the node content. This pins correctness across the redundant-clone removal
/// in <c>SerializeSnapshot</c>.
/// </summary>
/// <remarks>
/// Why the clone was redundant: every <c>NodeContext</c> call site passes
/// <c>dataContext.Get&lt;JsonNode&gt;("$")</c>, which already deep-clones by contract, so the node the
/// debugger receives is an exclusively-owned snapshot; <c>ToJsonString</c> is read-only, so cloning
/// it again inside the debugger changed nothing. On .NET 10 the second clone was also allocation-free
/// in practice — the node is element-backed (from <c>element.Deserialize&lt;JsonNode&gt;</c>), and
/// <c>DeepClone</c> of an element-backed node is ~free (measured: serialize-only ≈ clone-then-serialize
/// for the real shape; a hand-built materialized tree clones ~8× heavier). So this removal is dead-code
/// hygiene / insurance against a materialized node ever being passed, NOT a measurable win.
/// </remarks>
public class DefaultPipelineDebuggerSnapshotTests
{
    private static DefaultPipelineDebugger NewDebugger()
    {
        var debugger = new DefaultPipelineDebugger(NullLoggerFactory.Instance);
        debugger.RegisterPipelineRtEntityId(
            new RtEntityId("Test/Pipeline", OctoObjectId.GenerateNewId()), Guid.NewGuid());
        return debugger;
    }

    [Fact]
    public void LogOutput_Snapshot_PreservesNodeContent()
    {
        var debugger = NewDebugger();
        var node = JsonNode.Parse("""{"a":1,"b":["x","y"],"c":null,"nested":{"d":2.5}}""")!;

        debugger.LogOutput("0:node", new NodePath("node"), "desc", 0, node);

        var dp = debugger.GetDebugInformation().DebugPoints.Single();
        Assert.NotNull(dp.Output);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(dp.Output!), node),
            $"Snapshot output did not round-trip to the original node. Output: {dp.Output}");
    }

    [Fact]
    public void LogInput_Snapshot_PreservesNodeContent()
    {
        var debugger = NewDebugger();
        var node = JsonNode.Parse("""{"messages":[{"id":1},{"id":2}]}""")!;

        debugger.LogInput("0:node", new NodePath("node"), "desc", 0, node);

        var dp = debugger.GetDebugInformation().DebugPoints.Single();
        Assert.NotNull(dp.Input);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(dp.Input!), node),
            $"Snapshot input did not round-trip to the original node. Input: {dp.Input}");
    }
}
