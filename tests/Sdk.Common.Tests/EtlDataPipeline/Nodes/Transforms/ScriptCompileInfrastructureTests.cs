using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.CodeAnalysis;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

/// <summary>AB#7063: shared metadata references and serialised compiles.</summary>
public class ScriptMetadataReferencesTests
{
    // A new, distinct MetadataReference instance per call (Roslyn reads the file lazily, so this is cheap).
    private static MetadataReference Fake(string location) =>
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    [Fact]
    public void GetReferences_BuildsOnceAndReusesTheSameArray()
    {
        var calls = 0;
        var sut = new ScriptMetadataReferences(() => ["/a.dll", "/b.dll"], l => { calls++; return Fake(l); });

        var first = sut.GetReferences();
        var second = sut.GetReferences();
        var third = sut.GetReferences();

        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Equal(2, calls);
        Assert.Equal(1, sut.BuildCount);
    }

    [Fact]
    public void GetReferences_NewAssembly_RebuildsButKeepsExistingReferenceInstances()
    {
        var locations = new List<string> { "/a.dll", "/b.dll" };
        var calls = 0;
        var sut = new ScriptMetadataReferences(() => locations.ToList(), l => { calls++; return Fake(l); });

        var before = sut.GetReferences();
        locations.Add("/late.dll");
        var after = sut.GetReferences();

        Assert.NotSame(before, after);
        Assert.Equal(3, after.Count);
        Assert.Same(before[0], after[0]);
        Assert.Same(before[1], after[1]);
        Assert.Equal(3, calls);
        Assert.Equal(2, sut.BuildCount);
        Assert.Same(after, sut.GetReferences());
    }

    [Fact]
    public void GetReferences_SameCountDifferentAssembly_Rebuilds()
    {
        var locations = new List<string> { "/a.dll", "/b.dll" };
        var sut = new ScriptMetadataReferences(() => locations.ToList(), Fake);

        var before = sut.GetReferences();
        locations[1] = "/c.dll";
        var after = sut.GetReferences();

        Assert.NotSame(before, after);
        Assert.Equal(2, sut.BuildCount);
    }

    [Fact]
    public async Task GetReferences_ConcurrentCallers_BuildOnceAndShareTheArray()
    {
        var calls = 0;
        var sut = new ScriptMetadataReferences(
            () => Enumerable.Range(0, 200).Select(i => $"/{i}.dll").ToList(),
            l => { Interlocked.Increment(ref calls); return Fake(l); });

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => sut.GetReferences())));

        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(200, calls);
        Assert.Equal(1, sut.BuildCount);
    }

    [Fact]
    public void Shared_ContainsLoadedAssemblies()
    {
        var references = ScriptMetadataReferences.Shared.GetReferences();

        Assert.Contains(references, r => r.Display == typeof(object).Assembly.Location);
        Assert.Same(references, ScriptMetadataReferences.Shared.GetReferences());
    }
}

public class ScriptCompileCoordinatorTests
{
    [Fact]
    public async Task Run_SerialisesParallelCompiles()
    {
        var running = 0;
        var maxRunning = 0;
        var reclaimed = 0;
        var sut = new ScriptCompileCoordinator(() => Interlocked.Increment(ref reclaimed));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => sut.Run(() =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref running);
            return i;
        }))));

        Assert.Equal(1, maxRunning);
        Assert.Equal(8, reclaimed);
    }

    [Fact]
    public void Run_ReclaimsAfterEachCompileAndBeforeTheNextStarts()
    {
        var log = new List<string>();
        var sut = new ScriptCompileCoordinator(() => log.Add("reclaim"));

        sut.Run(() => { log.Add("compile1"); return 1; });
        sut.Run(() => { log.Add("compile2"); return 2; });

        Assert.Equal(["compile1", "reclaim", "compile2", "reclaim"], log);
    }

    [Fact]
    public void Run_CompileThrows_ReclaimsReleasesGateAndRethrows()
    {
        var reclaimed = 0;
        var sut = new ScriptCompileCoordinator(() => reclaimed++);

        Assert.Throws<InvalidOperationException>(() => sut.Run<int>(() => throw new InvalidOperationException("boom")));
        Assert.Equal(5, sut.Run(() => 5));
        Assert.Equal(2, reclaimed);
    }

    [Fact]
    public void Run_ReclaimThrows_DoesNotChangeResultOrBlockTheGate()
    {
        var sut = new ScriptCompileCoordinator(() => throw new InvalidOperationException("trim failed"));

        Assert.Equal(7, sut.Run(() => 7));
        Assert.Equal(8, sut.Run(() => 8));
    }

    [Fact]
    public void TryMallocTrim_NeverThrows()
    {
        var result = ScriptCompileCoordinator.TryMallocTrim();

        if (!OperatingSystem.IsLinux())
        {
            Assert.False(result);
        }
    }

    [Fact]
    public void ReclaimMemory_NeverThrows()
    {
        ScriptCompileCoordinator.ReclaimMemory();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
