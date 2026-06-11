using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// Collection for allocation/memory gates. These tests sample process-wide GC counters
/// (<see cref="System.GC.GetTotalAllocatedBytes(bool)"/> / <see cref="System.GC.GetTotalMemory(bool)"/>),
/// which are corrupted by concurrent allocations from tests running in parallel on other threads.
/// <c>DisableParallelization</c> makes this collection run in isolation (no other collection runs
/// concurrently) and its members run sequentially, so each measurement window contains only its own
/// work. Without this, gates like <c>TypedGetAllocationGate</c> and <c>ForEachMemoryBenchmark</c>
/// fail intermittently in the full suite while passing in isolation.
/// </summary>
[CollectionDefinition("AllocationGates", DisableParallelization = true)]
public sealed class AllocationGatesCollection;
