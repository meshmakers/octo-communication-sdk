using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Pins that a typed <c>Get&lt;T&gt;</c> on the immutable root base deserializes DIRECTLY from the
/// zero-copy <see cref="JsonElement"/> instead of first materialising an intermediate
/// <see cref="JsonNode"/> DOM per call (the element→node→T double round-trip the EDA trace caught
/// as a per-read ~40 MB <c>AllocateUninitializedArray</c> + <c>TranscodeHelper</c>).
/// </summary>
/// <remarks>
/// Compares the actual <c>Get&lt;T&gt;</c> (element-direct after the fix) against the OLD per-call
/// path reproduced explicitly — a fresh <c>element.Deserialize&lt;JsonNode&gt;</c> (== the former
/// <c>DataOverlay.TryRead</c>→<c>JsonDetach.ToNode</c>) followed by <c>node.Deserialize&lt;T&gt;</c>,
/// both via the same <see cref="SystemTextJsonOptions.Default"/>. The clean base never lifts, so
/// the old path paid that ToNode on EVERY read. Element-direct must allocate well under half. RED
/// before the fix (Get<T> == the old double round-trip), GREEN after.
/// </remarks>
[Collection("AllocationGates")]
public class TypedGetAllocationGate
{
    private static string BigArrayDoc()
    {
        var sb = new StringBuilder(1_700_000);
        sb.Append("{\"items\":[");
        for (var i = 0; i < 200_000; i++) { if (i > 0) sb.Append(','); sb.Append(i); }
        sb.Append("]}");
        return sb.ToString();
    }

    private static long Measure(Func<object?> f)
    {
        for (var i = 0; i < 3; i++) f();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        f();
        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    [Fact]
    public void TypedGet_OnCleanBase_IsElementDirect_NotDoubleRoundTrip()
    {
        var json = BigArrayDoc();
        using var doc = JsonDocument.Parse(json);
        var itemsEl = doc.RootElement.GetProperty("items");
        using var ctx = new DataContextImpl(doc.RootElement);

        var getAlloc = Measure(() => ctx.Get<int[]>("$.items"));
        // The OLD per-call path on a never-lifted base: fresh element→node (ToNode) then node→T.
        var oldRoundTripAlloc = Measure(() =>
            itemsEl.Deserialize<JsonNode>(SystemTextJsonOptions.Default)!.Deserialize<int[]>(SystemTextJsonOptions.Default));

        // Frozen absolute ceiling. Element-direct Get<int[]> of 200k ints measures ≈ 2.9 MB on
        // net10.0; the old element→node→T double round-trip (reproduced here as oldRoundTripAlloc,
        // ≈ 6.3 MB) is well above it, so a regression back to per-call node materialization trips
        // the ceiling. Deliberately ABSOLUTE rather than relative: on net10.0 JsonNode.Deserialize
        // narrowed the node-vs-element gap (getAlloc*2 ≈ 0.88×oldRoundTrip — only ~12% margin), so a
        // relative "< half" check is too tight to be stable. (See the LiftedRead/option-a finding.)
        Assert.True(getAlloc < 4_500_000,
            $"Get<int[]> element-direct allocated {getAlloc / (1024.0 * 1024):F1} MB, over the 4.5 MB " +
            $"frozen ceiling (old element→node→T double round-trip ≈ {oldRoundTripAlloc / (1024.0 * 1024):F1} MB). " +
            "Typed-read allocation regressed — likely a reintroduced intermediate JsonNode DOM.");
    }
}
