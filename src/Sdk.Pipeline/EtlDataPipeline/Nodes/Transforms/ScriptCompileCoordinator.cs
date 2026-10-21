using System.Runtime;
using System.Runtime.InteropServices;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Serialises <c>ExecuteCSharp</c> compiles within the process and returns the native memory a compile
/// leaves behind (AB#7063).
/// <para>
/// A Roslyn compile allocates hundreds of MB of unmanaged metadata that the runtime only frees after a
/// gen2 GC with finalizers, and glibc then keeps the freed pages. Parallel first executions would stack
/// those peaks, so compiles run one at a time, and after each one a full blocking GC plus
/// <c>malloc_trim(0)</c> (Linux/glibc only) hands the memory back to the OS before the next compile starts.
/// The compile happens once per distinct script, so the pause is paid on first use only.
/// </para>
/// </summary>
internal sealed class ScriptCompileCoordinator
{
    /// <summary>The instance used by <see cref="ExecuteCSharpNode"/>.</summary>
    internal static ScriptCompileCoordinator Shared { get; } = new(ReclaimMemory);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action _reclaim;

    internal ScriptCompileCoordinator(Action reclaim)
    {
        _reclaim = reclaim;
    }

    /// <summary>Runs <paramref name="compile"/> exclusively, then reclaims memory (also when it throws).</summary>
    internal T Run<T>(Func<T> compile)
    {
        _gate.Wait();
        try
        {
            return compile();
        }
        finally
        {
            try
            {
                _reclaim();
            }
            catch
            {
                // Reclaiming is best effort and must never change the outcome of a compile.
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MallocTrimDelegate(nuint pad);

    /// <summary>Full blocking gen2 GC with LOH compaction, then <see cref="TryMallocTrim"/>.</summary>
    internal static void ReclaimMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        // Finalizers release the native metadata readers; collect again so what they freed is trimmed too.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        TryMallocTrim();
    }

    /// <summary>
    /// Calls glibc <c>malloc_trim(0)</c>. Returns false (never throws) off Linux, on musl, or when the
    /// symbol cannot be resolved.
    /// </summary>
    internal static bool TryMallocTrim()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            if (!NativeLibrary.TryLoad("libc.so.6", out var libc)
                || !NativeLibrary.TryGetExport(libc, "malloc_trim", out var export))
            {
                return false;
            }

            Marshal.GetDelegateForFunctionPointer<MallocTrimDelegate>(export)(0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
