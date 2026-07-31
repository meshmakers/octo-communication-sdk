namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Execution;

/// <summary>
/// Default <see cref="IPipelineScratchSpace"/> backed by a per-execution directory
/// under <see cref="RootDirectory"/>. The directory is created lazily on first use
/// and removed on <see cref="DisposeAsync"/>.
/// </summary>
public sealed class PipelineScratchSpace : IPipelineScratchSpace
{
    /// <summary>
    /// Root under which every execution's scratch directory is created. All scratch
    /// directories share this root so <see cref="SweepStaleDirectories"/> can find and
    /// delete ones orphaned by a hard crash. Overridable at startup (e.g. to point at a
    /// sized ephemeral volume) before any pipeline runs.
    /// </summary>
    public static string RootDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "octo-pipeline-scratch");

    private readonly string _directory;
    private readonly HashSet<string> _tokens = [];
    private readonly object _gate = new();
    private bool _directoryCreated;
    private bool _disposed;

    /// <summary>Creates a scratch space with a unique directory under <see cref="RootDirectory"/>.</summary>
    public PipelineScratchSpace()
    {
        _directory = Path.Combine(RootDirectory, Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public string CreateFile(string? extension = null)
    {
        var token = Guid.NewGuid().ToString("N") + NormalizeExtension(extension);
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureDirectory();
            // Materialize an empty file so GetLength/Exists are well-defined before a write.
            using (File.Create(ResolvePath(token)))
            {
            }

            _tokens.Add(token);
        }

        return token;
    }

    /// <inheritdoc />
    public Stream OpenWrite(string token)
    {
        return new FileStream(ResolveKnownPath(token), FileMode.Create, FileAccess.Write, FileShare.None);
    }

    /// <inheritdoc />
    public Stream OpenRead(string token)
    {
        return new FileStream(ResolveKnownPath(token), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <inheritdoc />
    public long GetLength(string token)
    {
        return new FileInfo(ResolveKnownPath(token)).Length;
    }

    /// <inheritdoc />
    public bool Exists(string token)
    {
        lock (_gate)
        {
            return _tokens.Contains(token) && File.Exists(ResolvePath(token));
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        bool deleteDirectory;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            deleteDirectory = _directoryCreated;
        }

        if (deleteDirectory)
        {
            TryDeleteDirectory(_directory);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Deletes scratch directories under <see cref="RootDirectory"/> whose last-write
    /// time is older than <paramref name="olderThan"/>. Belt-and-suspenders cleanup for
    /// directories orphaned by a process crash / OOM that skipped the per-execution
    /// dispose. Best-effort: never throws. Call once at adapter startup.
    /// </summary>
    public static void SweepStaleDirectories(TimeSpan olderThan)
    {
        try
        {
            if (!Directory.Exists(RootDirectory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - olderThan;
            foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                    {
                        TryDeleteDirectory(directory);
                    }
                }
                catch
                {
                    // best-effort per directory
                }
            }
        }
        catch
        {
            // best-effort: a sweep failure must never break adapter startup
        }
    }

    private void EnsureDirectory()
    {
        if (_directoryCreated)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        _directoryCreated = true;
    }

    private string ResolvePath(string token)
    {
        return Path.Combine(_directory, token);
    }

    private string ResolveKnownPath(string token)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_tokens.Contains(token))
            {
                throw new InvalidOperationException(
                    $"Unknown scratch file token '{token}'. Tokens must be obtained from {nameof(CreateFile)} on the same scratch space.");
            }
        }

        return ResolvePath(token);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PipelineScratchSpace));
        }
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim().TrimStart('.');
        // Keep it filename-safe and cosmetic only.
        return trimmed.Length == 0 || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? string.Empty
            : "." + trimmed;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // best-effort — the startup sweep reclaims anything left behind
        }
    }
}
