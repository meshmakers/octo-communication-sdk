using System.Collections.Concurrent;
using System.Text;
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

    private readonly DebugPipelineLogger _debugPipelineLogger;
    private readonly ConcurrentDictionary<string, DebugPointDto> _debugPoints = new();

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
    public void BeginPipelineExecution()
    {
        _debugPipelineLogger.Clear();
    }

    /// <inheritdoc />
    public virtual Task EndPipelineExecutionAsync()
    {
        return Task.CompletedTask;
    }

    private static string? SerializeSnapshot(JsonNode? data)
    {
        if (data == null) return null;

        // Debug capture must NEVER crash pipeline execution. SerializeSnapshot is the single choke
        // point for LogInput/LogOutput/RecordDryRunIntent, so all bounding and backstopping lives here.
        //
        // No DeepClone before serialising: writing is read-only and runs synchronously here, so the
        // node is fully consumed at capture time before any later mutation. NodeContext's debug capture
        // passes IDebugSnapshotSource.GetDebugSnapshot(), which already returns an owned clone for an
        // iteration child (aliases folded in) and the live "$" view on a root context (safe to read once
        // synchronously). Cloning again copied a whole document tree for nothing.
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
        _debugPoints.AddOrUpdate(id, _ => new DebugPointDto(id, path, description, sequenceNumber)
        {
            Input = SerializeSnapshot(inputData)
        }, (key, value) =>
        {
            value.Input = SerializeSnapshot(inputData);
            return value;
        });
    }

    /// <inheritdoc />
    public void LogOutput(string id, NodePath path, string? description, uint sequenceNumber, JsonNode? outputData)
    {
        _debugPoints.AddOrUpdate(id, _ => new DebugPointDto(id, path, description, sequenceNumber)
        {
            Output = SerializeSnapshot(outputData)
        }, (key, value) =>
        {
            value.Output = SerializeSnapshot(outputData);
            return value;
        });
    }

    /// <inheritdoc />
    public void RecordDryRunIntent(string id, NodePath path, string? description, uint sequenceNumber,
        string nodeTypeName, JsonNode? intentData)
    {
        var serialised = SerializeSnapshot(intentData);
        _debugPoints.AddOrUpdate(id, _ => new DebugPointDto(id, path, description, sequenceNumber)
        {
            DryRunIntent = serialised,
            DryRunNodeTypeName = nodeTypeName
        }, (key, value) =>
        {
            value.DryRunIntent = serialised;
            value.DryRunNodeTypeName = nodeTypeName;
            return value;
        });
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

        var debuggers = new DebugInformationRoot
        {
            PipelineRtEntityId = PipelineRtEntityId ?? throw new Exception("PipelineRtEntityId is not set"),
            PipelineExecutionId = PipelineExecutionId ?? throw new Exception("PipelineExecutionId is not set"),
            DebugPoints = _debugPoints.Values.ToList()
        };
        return debuggers;
    }
}
