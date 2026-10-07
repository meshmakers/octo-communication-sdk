using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// Implements a default pipeline debugger
/// </summary>
public class DefaultPipelineDebugger : IPipelineDebugger
{
    private static readonly JsonSerializerOptions DebugSerializerOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Upper bound, in UTF-8 bytes, for a captured node snapshot. Debug capture is a diagnostic
    /// convenience and must NEVER influence pipeline execution; a single node can carry a
    /// multi-million-element payload (real case: 2,805,504 datapoints) whose serialised JSON exceeds
    /// 2 GB. Materialising that string throws (e.g. <see cref="OverflowException" /> inside
    /// <c>Encoding.GetString</c> over a buffer larger than <see cref="int.MaxValue" />) and aborts the
    /// whole pipeline. Above this cap the snapshot is replaced by a short placeholder instead.
    /// </summary>
    private const int MaxSnapshotBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Placeholder stored once the total capture budget is exhausted (see
    /// <see cref="MaxTotalRetainedSnapshotChars"/>).
    /// </summary>
    private const string TotalBudgetPlaceholder =
        "<debug snapshot omitted: total debug capture budget exhausted>";

    private readonly DebugPipelineLogger _debugPipelineLogger;
    private readonly ConcurrentDictionary<string, DebugPointDto> _debugPoints = new();
    private readonly ConcurrentDictionary<PipelineSecretRegistry, byte> _secretRegistries =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, RedactedPathSlots> _redactedPaths = new();
    private long _retainedSnapshotChars;

    /// <summary>
    /// Upper bound for the TOTAL characters retained across ALL captured snapshots of one
    /// execution. Loop nodes register a debug point per ITERATION (the node id embeds the
    /// iteration index), so the per-snapshot cap alone still let overall capture grow without
    /// bound on large runs and OOM-kill the adapter (AB#4662: a 4-level nested ForEach over a
    /// few thousand entities exhausted a 3Gi pod). Once the budget is exhausted, later captures
    /// store <see cref="TotalBudgetPlaceholder"/> instead — the earliest iterations win, which
    /// is what an operator stepping through a loop inspects anyway. ~32M chars ≈ 64 MB retained.
    /// Internal-settable for tests.
    /// </summary>
    internal long MaxTotalRetainedSnapshotChars { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// The pipeline runtime entity id
    /// </summary>
    // ReSharper disable once NotAccessedField.Global
    protected RtEntityId? PipelineRtEntityId;

    /// <summary>
    /// The pipeline execution id, which is a guid that identifies the pipeline execution instance
    /// </summary>
    protected Guid? PipelineExecutionId;

    /// <summary>
    /// Creates a new instance of <see cref="T:Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger.DefaultPipelineDebugger" />
    /// </summary>
    /// <param name="loggerFactory"></param>
    public DefaultPipelineDebugger(ILoggerFactory loggerFactory)
    {
        _debugPipelineLogger = new DebugPipelineLogger(loggerFactory);
        Logger = _debugPipelineLogger;
    }

    /// <inheritdoc />
    public IPipelineLogger Logger { get; }

    /// <inheritdoc />
    public void RegisterPipelineRtEntityId(RtEntityId pipelineRtEntityId, Guid pipelineExecutionId)
    {
        PipelineRtEntityId = pipelineRtEntityId;
        PipelineExecutionId = pipelineExecutionId;
    }

    /// <inheritdoc />
    public void AddSecretRegistry(PipelineSecretRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _secretRegistries.TryAdd(registry, 0);
    }

    /// <summary>
    /// Masks every value registered as secret in this execution (AB#5538) before a snapshot is
    /// serialised. Returns the same node when nothing is registered or nothing matches, so the
    /// snapshot path stays clone-free in the common case.
    /// </summary>
    private JsonNode? RedactSecrets(JsonNode? data, string rootPath, out IReadOnlyList<string>? redactedPaths)
    {
        redactedPaths = null;
        if (data == null || _secretRegistries.IsEmpty)
        {
            return data;
        }

        // Several registries (trigger + execution root) may mask the same string: a set keeps each
        // path once (ordinal order).
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var registry in _secretRegistries.Keys)
        {
            data = registry.Redact(data, rootPath, paths);
        }

        if (paths.Count > 0)
        {
            redactedPaths = paths.ToArray();
        }

        return data;
    }

    /// <inheritdoc />
    public void BeginPipelineExecution()
    {
        _debugPipelineLogger.Clear();
    }

    /// <inheritdoc />
    public virtual Task EndPipelineExecutionAsync()
    {
        return Task.CompletedTask;
    }

    private string? SerializeSnapshot(JsonNode? data, string rootPath, out IReadOnlyList<string>? redactedPaths)
    {
        redactedPaths = null;
        if (data == null) return null;

        // Total-budget gate BEFORE serialising: once exhausted, later captures skip the whole
        // clone/serialize cost and retain only a short placeholder (see
        // MaxTotalRetainedSnapshotChars). Placeholders are charged like any other retained
        // string so the replace-discount accounting in LogInput/LogOutput stays symmetric.
        var result = Interlocked.Read(ref _retainedSnapshotChars) >= MaxTotalRetainedSnapshotChars
            ? TotalBudgetPlaceholder
            : SerializeRedactedSnapshot(data, rootPath, out redactedPaths);
        Interlocked.Add(ref _retainedSnapshotChars, result.Length);
        return result;
    }

    private string SerializeRedactedSnapshot(JsonNode data, string rootPath, out IReadOnlyList<string>? redactedPaths)
    {
        JsonNode redacted;
        try
        {
            redacted = RedactSecrets(data, rootPath, out redactedPaths)!;
        }
        catch (Exception ex)
        {
            // Fail closed: a snapshot that could not be redacted is not shown at all.
            redactedPaths = null;
            return $"<debug snapshot unavailable: {ex.GetType().Name}>";
        }

        var serialized = SerializeSnapshotCore(redacted);
        if (redactedPaths != null && serialized.StartsWith('<'))
        {
            // The snapshot was replaced by an "omitted" text: no path inside it exists any more.
            redactedPaths = null;
        }

        return serialized;
    }

    /// <summary>
    /// Credits the budget back for a snapshot string that is being replaced on an existing
    /// debug point, so re-captures of the same node id don't double-charge.
    /// </summary>
    private void DiscountRetained(string? replaced)
    {
        if (replaced is not null)
        {
            Interlocked.Add(ref _retainedSnapshotChars, -replaced.Length);
        }
    }

    private static string SerializeSnapshotCore(JsonNode data)
    {
        // Debug capture must NEVER crash pipeline execution. SerializeSnapshot is the single choke
        // point for LogInput/LogOutput/RecordDryRunIntent, so all bounding and backstopping lives here.
        //
        // No DeepClone before serialising: writing is read-only and runs synchronously here, so the
        // node is fully consumed at capture time before any later mutation. NodeContext's debug capture
        // passes IDebugSnapshotSource.GetDebugSnapshot(), which already returns an owned clone for an
        // iteration child (alias placeholders folded in) and the live "$" view on a root context (safe
        // to read once synchronously). Cloning again copied a whole document tree for nothing.
        try
        {
            using var stream = new ByteBudgetStream(MaxSnapshotBytes);
            try
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    // Stream straight to a byte-budgeted writer; ByteBudgetStream aborts the write the
                    // moment the cap is passed, so a multi-GB node never materialises a giant string.
                    data.WriteTo(writer, DebugSerializerOptions);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (ByteBudgetExceededException)
            {
                return $"<debug snapshot omitted: output too large (> {MaxSnapshotBytes} bytes)>";
            }
        }
        catch (Exception ex)
        {
            // Last-resort backstop: ANY failure (OverflowException, OOM-ish, serializer errors) degrades
            // to a placeholder and is swallowed here so it can never propagate into await next(...).
            return $"<debug snapshot unavailable: {ex.GetType().Name}>";
        }
    }

    /// <summary>
    /// Thrown internally by <see cref="ByteBudgetStream" /> when a snapshot serialisation passes the
    /// configured byte budget. Caught inside <see cref="SerializeSnapshot" /> and never surfaced.
    /// </summary>
    private sealed class ByteBudgetExceededException : Exception;

    /// <summary>
    /// A write-only, in-memory stream that buffers bytes up to a fixed budget and throws
    /// <see cref="ByteBudgetExceededException" /> as soon as a write would exceed it. Used to bound
    /// snapshot serialisation without ever building an oversized buffer/string.
    /// </summary>
    private sealed class ByteBudgetStream(int maxBytes) : Stream
    {
        private readonly MemoryStream _inner = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public byte[] ToArray()
        {
            return _inner.ToArray();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureBudget(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureBudget(buffer.Length);
            _inner.Write(buffer);
        }

        private void EnsureBudget(int count)
        {
            if (_inner.Length + count > maxBytes)
            {
                throw new ByteBudgetExceededException();
            }
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <inheritdoc />
    public void LogInput(string id, NodePath path, string? description, uint sequenceNumber, JsonNode? inputData)
    {
        var serialized = SerializeSnapshot(inputData, InputRootPath, out var redactedPaths);
        _debugPoints.AddOrUpdate(id, _ => new DebugPointDto(id, path, description, sequenceNumber)
        {
            Input = serialized
        }, (key, value) =>
        {
            DiscountRetained(value.Input);
            value.Input = serialized;
            return value;
        });
        SetRedactedPaths(id, RedactedPathSlot.Input, redactedPaths);
    }

    /// <inheritdoc />
    public void LogOutput(string id, NodePath path, string? description, uint sequenceNumber, JsonNode? outputData)
    {
        var serialized = SerializeSnapshot(outputData, OutputRootPath, out var redactedPaths);
        _debugPoints.AddOrUpdate(id, _ => new DebugPointDto(id, path, description, sequenceNumber)
        {
            Output = serialized
        }, (key, value) =>
        {
            DiscountRetained(value.Output);
            value.Output = serialized;
            return value;
        });
        SetRedactedPaths(id, RedactedPathSlot.Output, redactedPaths);
    }

    /// <inheritdoc />
    public void RecordDryRunIntent(string id, NodePath path, string? description, uint sequenceNumber,
        string nodeTypeName, JsonNode? intentData)
    {
        var serialised = SerializeSnapshot(intentData, DryRunIntentRootPath, out var redactedPaths);
        _debugPoints.AddOrUpdate(id, _ => new DebugPointDto(id, path, description, sequenceNumber)
        {
            DryRunIntent = serialised,
            DryRunNodeTypeName = nodeTypeName
        }, (key, value) =>
        {
            DiscountRetained(value.DryRunIntent);
            value.DryRunIntent = serialised;
            value.DryRunNodeTypeName = nodeTypeName;
            return value;
        });
        SetRedactedPaths(id, RedactedPathSlot.DryRunIntent, redactedPaths);
    }

    /// <summary>
    /// JSONPath root of the input snapshot in the debug point delivered to Studio (handover §11).
    /// </summary>
    public const string InputRootPath = "$.input";

    /// <summary>
    /// JSONPath root of the output snapshot in the debug point delivered to Studio (handover §11).
    /// </summary>
    public const string OutputRootPath = "$.output";

    /// <summary>
    /// JSONPath root of the dry-run intent of a debug point.
    /// </summary>
    public const string DryRunIntentRootPath = "$.dryRunIntent";

    /// <summary>
    /// Returns the JSONPaths of the values that were masked as <c>***</c> in the snapshots of debug
    /// point <paramref name="nodeId" /> (AB#5538, handover §11, Q12): rooted at the snapshot object
    /// (<see cref="InputRootPath" />, <see cref="OutputRootPath" />, <see cref="DryRunIntentRootPath" />),
    /// a string with a secret masked inside it listed with its own path. <c>null</c> when nothing was
    /// masked.
    /// </summary>
    /// <param name="nodeId">The debug point (node) id</param>
    /// <returns>The paths, ordinal-sorted, or null</returns>
    public IReadOnlyList<string>? GetRedactedPaths(string nodeId)
    {
        return _redactedPaths.TryGetValue(nodeId, out var slots) ? slots.ToList() : null;
    }

    private void SetRedactedPaths(string id, RedactedPathSlot slot, IReadOnlyList<string>? paths)
    {
        if (paths == null)
        {
            // A re-capture without secrets replaces the earlier paths of the same slot.
            if (_redactedPaths.TryGetValue(id, out var existing))
            {
                existing.Set(slot, null);
            }

            return;
        }

        _redactedPaths.GetOrAdd(id, _ => new RedactedPathSlots()).Set(slot, paths);
    }

    private enum RedactedPathSlot
    {
        Input = 0,
        Output = 1,
        DryRunIntent = 2
    }

    private sealed class RedactedPathSlots
    {
        private readonly IReadOnlyList<string>?[] _slots = new IReadOnlyList<string>?[3];

        public void Set(RedactedPathSlot slot, IReadOnlyList<string>? paths)
        {
            lock (_slots)
            {
                _slots[(int)slot] = paths;
            }
        }

        public IReadOnlyList<string>? ToList()
        {
            lock (_slots)
            {
                var all = _slots.Where(s => s != null).SelectMany(s => s!).ToArray();
                return all.Length == 0 ? null : all;
            }
        }
    }

    /// <inheritdoc />
    public DebugInformationRoot GetDebugInformation()
    {
        foreach (var debugMessageGrouping in _debugPipelineLogger.Messages.GroupBy(x => x.NodeId))
        {
            if (_debugPoints.TryGetValue(debugMessageGrouping.Key, out var debugPoint))
            {
                debugPoint.Messages = debugMessageGrouping.ToList();
            }
        }

        // Q12 (AB#5544): the transport DTO carries the masked paths to the controller.
        foreach (var (id, debugPoint) in _debugPoints)
        {
            debugPoint.RedactedPaths = GetRedactedPaths(id);
        }

        var debuggers = new DebugInformationRoot
        {
            PipelineRtEntityId = PipelineRtEntityId ?? throw new Exception("PipelineRtEntityId is not set"),
            PipelineExecutionId = PipelineExecutionId ?? throw new Exception("PipelineExecutionId is not set"),
            DebugPoints = _debugPoints.Values.ToList(),
            RedactedPaths = _debugPoints.Keys
                .Select(id => (Id: id, Paths: GetRedactedPaths(id)))
                .Where(x => x.Paths != null)
                .ToDictionary(x => x.Id, x => x.Paths!, StringComparer.Ordinal)
        };
        return debuggers;
    }
}
