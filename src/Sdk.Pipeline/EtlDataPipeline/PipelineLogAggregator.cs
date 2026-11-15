using System.Collections.Concurrent;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Collects repeated per-record conditions of one pipeline execution and reports each of them
/// once, with a count, when the execution ends (AB#9006).
/// </summary>
/// <remarks>
/// <para>
/// A node inside a <c>ForEach@1</c> runs once per record. A condition it would log per record —
/// for example a missing optional input — turns into one log line per record, which on prod-1 was
/// ~808k warnings a day from <c>ExecuteCSharp@1</c> alone. A node calls <see cref="Record" /> per
/// occurrence instead; the orchestrator calls <see cref="Flush" /> once at the end of the
/// execution (success or failure), which invokes each entry's report callback once with the
/// number of occurrences.
/// </para>
/// <para>
/// One instance per execution, shared by every node context of it, exactly like
/// <see cref="PipelineSecretRegistry" />: a child node context inherits its parent's, a root node
/// context creates one. Thread-safe, because <c>ForEach@1</c> iterations may run in parallel.
/// </para>
/// <para>
/// Entries are keyed by an <b>owner</b> compared by reference — normally the node configuration
/// object, which is the same instance for every iteration of a node, whereas the node path carries
/// the iteration index — plus a string key within that owner.
/// </para>
/// </remarks>
public sealed class PipelineLogAggregator
{
    private readonly ConcurrentDictionary<EntryKey, Entry> _entries = new();

    /// <summary>
    /// Records one occurrence.
    /// </summary>
    /// <param name="owner">The owner of the entry, compared by reference (normally the node configuration)</param>
    /// <param name="key">The key of the condition within the owner (e.g. the argument name)</param>
    /// <param name="report">
    /// Called once by <see cref="Flush" /> with the number of occurrences. Only the callback of the
    /// first occurrence is kept.
    /// </param>
    /// <returns>The number of occurrences recorded so far, including this one</returns>
    public long Record(object owner, string key, Action<long> report)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(report);

        var entry = _entries.GetOrAdd(new EntryKey(owner, key), static (_, r) => new Entry(r), report);
        return Interlocked.Increment(ref entry.Count);
    }

    /// <summary>
    /// Gets the number of occurrences recorded for an entry that has not been flushed yet.
    /// </summary>
    /// <param name="owner">The owner, compared by reference</param>
    /// <param name="key">The key within the owner</param>
    /// <returns>The count, or 0 when nothing was recorded</returns>
    public long GetCount(object owner, string key)
    {
        return _entries.TryGetValue(new EntryKey(owner, key), out var entry) ? Interlocked.Read(ref entry.Count) : 0;
    }

    /// <summary>
    /// Reports every recorded entry once and clears them. A report callback that throws is logged
    /// as an error on <paramref name="nodeContext" /> and does not stop the other reports.
    /// </summary>
    /// <param name="nodeContext">The context that logs a failing report callback (normally the root context)</param>
    public void Flush(INodeContext nodeContext)
    {
        foreach (var key in _entries.Keys.ToArray())
        {
            if (!_entries.TryRemove(key, out var entry))
            {
                continue;
            }

            try
            {
                entry.Report(Interlocked.Read(ref entry.Count));
            }
            catch (Exception e)
            {
                nodeContext.Error(e, "Error reporting aggregated log entry '{Key}'", key.Key);
            }
        }
    }

    private sealed class Entry(Action<long> report)
    {
        public readonly Action<long> Report = report;
        public long Count;
    }

    private readonly struct EntryKey(object owner, string key) : IEquatable<EntryKey>
    {
        private readonly object _owner = owner;

        public string Key { get; } = key;

        public bool Equals(EntryKey other) =>
            ReferenceEquals(_owner, other._owner) && string.Equals(Key, other.Key, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is EntryKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_owner),
                StringComparer.Ordinal.GetHashCode(Key));
    }
}
