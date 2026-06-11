using System.Collections.Concurrent;
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
        // No DeepClone before serialising: ToJsonString is read-only and runs synchronously here, so
        // the node is fully consumed at capture time (turned into a string) before any later mutation.
        // NodeContext's debug capture passes IDebugSnapshotSource.GetDebugSnapshot(), which already
        // returns an owned clone for an iteration child (aliases folded in) and the live "$" view on a
        // root context (safe to read once synchronously). Cloning again copied a whole document tree
        // for nothing — an ~8× allocation landmine the moment a materialized (non-element-backed) node
        // is passed.
        return data.ToJsonString(DebugSerializerOptions);
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
