using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// Interface for the pipeline debugger
/// </summary>
public interface IPipelineDebugger
{
    /// <summary>
    /// Returns the debugging logger
    /// </summary>
    IPipelineLogger Logger { get; }

    /// <summary>
    /// Registers the pipeline runtime entity id
    /// </summary>
    /// <param name="pipelineRtEntityId">Entity id of the pipeline</param>
    /// <param name="pipelineExecutionId">Guid that identifies the pipeline execution instance</param>
    void RegisterPipelineRtEntityId(RtEntityId pipelineRtEntityId, Guid pipelineExecutionId);

    /// <summary>
    /// Signals the beginning of the pipeline execution
    /// </summary>
    void BeginPipelineExecution();

    /// <summary>
    /// Signals the end of the pipeline execution
    /// </summary>
    Task EndPipelineExecutionAsync();

    /// <summary>
    /// Logs the input of a node
    /// </summary>
    /// <param name="id">ID of the node</param>
    /// <param name="path">Path to the node</param>
    /// <param name="description">Description of the node</param>
    /// <param name="sequenceNumber">Sequence number of the node within a transformation list</param>
    /// <param name="inputData">Input data before a node is processed</param>
    void LogInput(string id, NodePath path, string? description, uint sequenceNumber, JsonNode? inputData);

    /// <summary>
    /// Logs the output of a node
    /// </summary>
    /// <param name="id">ID of the node</param>
    /// <param name="path">Path to the node</param>
    /// <param name="description">Description of the node</param>
    /// <param name="sequenceNumber">Sequence number of the node within a transformation list</param>
    /// <param name="outputData">Output data after a node is processed</param>
    void LogOutput(string id, NodePath path, string? description, uint sequenceNumber, JsonNode? outputData);

    /// <summary>
    /// Records a dry-run "would-have-written" intent for a node — written into the
    /// debug point's <see cref="DebugPointDto.DryRunIntent"/> /
    /// <see cref="DebugPointDto.DryRunNodeTypeName"/> fields, analogous to how
    /// <see cref="LogInput"/> / <see cref="LogOutput"/> capture per-node data.
    /// Called by Load nodes via <see cref="Nodes.INodeContext.RecordDryRunIntent(string, object)"/>
    /// when the pipeline's <c>IPipelineExecutionMode.IsDryRun</c> is true.
    /// </summary>
    /// <param name="id">ID of the node</param>
    /// <param name="path">Path to the node</param>
    /// <param name="description">Description of the node (or null)</param>
    /// <param name="sequenceNumber">Sequence number of the node within a transformation list</param>
    /// <param name="nodeTypeName">Node type name (e.g. <c>ApplyChanges@1</c>)</param>
    /// <param name="intentData">JSON-serialised would-be payload, or null if the
    /// payload failed to serialise (the orchestrator logs a warning in that case)</param>
    void RecordDryRunIntent(string id, NodePath path, string? description, uint sequenceNumber,
        string nodeTypeName, JsonNode? intentData);

    /// <summary>
    /// Hands the debugger the secret registry of an execution (AB#5538). Every value registered in it
    /// — now or later in the run — is masked in the snapshots and dry-run intents the debugger captures
    /// from then on. Called by the root <see cref="Nodes.NodeContext" />; a debugger may receive more
    /// than one registry (a trigger's root context and the execution's root context) and must mask
    /// the values of all of them. The default implementation ignores the registry, so a custom
    /// debugger keeps compiling — but shows revealed secrets until it implements this.
    /// </summary>
    /// <param name="registry">The registry of the execution</param>
    void AddSecretRegistry(PipelineSecretRegistry registry)
    {
    }

    /// <summary>
    /// Gets the debug information
    /// </summary>
    /// <returns></returns>
    DebugInformationRoot GetDebugInformation();
}
