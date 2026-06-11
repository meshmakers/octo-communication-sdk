using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.Benchmarks;

/// <summary>
/// Zero-copy proof: a targeted read-only <see cref="IDataContext.SelectMatches"/> for a
/// single element (<c>$.items[0]</c>) on a ROOT context must NOT scale with document
/// size. The root read path evaluates JSONPath against the backing
/// <see cref="JsonElement"/> directly (no whole-document clone), so reading one element
/// out of a tiny doc and out of a 40x-larger doc should allocate roughly the same amount.
///
/// This gate guards against a regression where unifying the context implementations
/// reintroduces a full-document materialization on the read path.
/// </summary>
[Collection("AllocationGates")]
public class DataContextBigDocReadAllocationGate
{
    private readonly ITestOutputHelper _output;

    public DataContextBigDocReadAllocationGate(ITestOutputHelper output)
    {
        _output = output;
    }

    private static JsonDocument BuildDoc(int itemCount)
    {
        var arr = new JsonArray();
        for (var i = 0; i < itemCount; i++)
        {
            arr.Add(new JsonObject { ["id"] = i, ["v"] = i * 2 });
        }
        var root = new JsonObject
        {
            ["items"] = arr,
            ["blob"] = new string('x', 200_000)
        };
        return JsonDocument.Parse(root.ToJsonString());
    }

    private static long MeasurePerCallAllocation(JsonDocument document)
    {
        using var ctx = new DataContextImpl(document.RootElement);

        // Warm up so JIT / first-call allocations are not charged.
        for (var w = 0; w < 3; w++)
        {
            foreach (var m in ctx.SelectMatches("$.items[0]")) m.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        const int iterations = 50;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            foreach (var m in ctx.SelectMatches("$.items[0]")) m.Dispose();
        }
        var after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / iterations;
    }

    [Fact]
    public void TargetedRead_DoesNotScaleWithDocumentSize()
    {
        using var smallDoc = BuildDoc(100);
        using var bigDoc = BuildDoc(4000);

        var allocSmall = MeasurePerCallAllocation(smallDoc);
        var allocBig = MeasurePerCallAllocation(bigDoc);

        _output.WriteLine($"Per-call allocation: small (n=100) = {allocSmall} bytes, " +
                          $"big (n=4000) = {allocBig} bytes");

        // Frozen absolute ceiling. The targeted zero-copy read detaches one small element and
        // measures ≈ 1.5 KB/call on net10.0, INDEPENDENT of document size (small n=100 and big
        // n=4000 both ≈ 1536 bytes). A regression that materializes the whole document per read
        // would allocate orders of magnitude more, far past the 50 KB ceiling. Absolute rather than
        // relative-to-allocSmall so creep that inflates both measurements equally cannot hide.
        Assert.True(allocBig < 50_000,
            $"Targeted read allocated {allocBig} bytes/call (frozen ceiling 50 KB) — likely lost " +
            $"zero-copy and is materializing with document size. small={allocSmall}, big={allocBig}.");
    }
}
