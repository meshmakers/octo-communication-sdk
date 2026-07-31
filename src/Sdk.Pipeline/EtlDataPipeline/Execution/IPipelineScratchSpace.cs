namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

/// <summary>
/// Per-execution scratch space for large binary artifacts.
/// <para>
/// Nodes that produce or consume large binaries (merged PDFs, ZIP archives, …)
/// should write them to a scratch file and pass a small handle token through the
/// JSON data context instead of carrying the bytes as base64. Base64 held in a
/// .NET UTF-16 string is roughly 2.66x the binary size and every large copy lands
/// on the Large Object Heap, which fragments and triggers OutOfMemoryException long
/// before the process limit is reached (prod-1 BMD handover export, AB#4642).
/// </para>
/// <para>
/// Handles are opaque tokens — the space owns the on-disk location, so nodes never
/// see (or can influence) a filesystem path. The whole space is deleted when the
/// pipeline execution ends (see <c>EtlDataOrchestrator.ExecutePipelineAsync</c>'s
/// finally block); a startup sweep (<see cref="PipelineScratchSpace.SweepStaleDirectories"/>)
/// removes directories left behind by a hard crash / OOM that skipped the finally.
/// </para>
/// </summary>
public interface IPipelineScratchSpace : IAsyncDisposable
{
    /// <summary>
    /// Creates a new, empty scratch file and returns its opaque token. The optional
    /// <paramref name="extension"/> is only cosmetic (helps debugging); it never
    /// affects where the file lives.
    /// </summary>
    string CreateFile(string? extension = null);

    /// <summary>Opens a scratch file for writing (creates/truncates it).</summary>
    Stream OpenWrite(string token);

    /// <summary>Opens a scratch file for reading.</summary>
    Stream OpenRead(string token);

    /// <summary>Returns the length in bytes of a scratch file.</summary>
    long GetLength(string token);

    /// <summary>Returns whether a scratch file with this token exists in this space.</summary>
    bool Exists(string token);
}
