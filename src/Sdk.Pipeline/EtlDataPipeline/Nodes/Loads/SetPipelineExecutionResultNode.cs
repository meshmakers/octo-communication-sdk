using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;

/// <summary>
/// Configuration for the SetPipelineExecutionResult node
/// </summary>
[NodeName("SetPipelineExecutionResult", 1)]
public record SetPipelineExecutionResultNodeConfiguration : PathNodeConfiguration
{
    /// <summary>
    /// Maximum length of the serialized output data in characters.
    /// If the result exceeds this limit, it will be truncated.
    /// Default: 1048576 (1 MB)
    /// </summary>
    [PropertyGroup("Options", 0)]
    public int MaxLength { get; set; } = 1048576;
}

/// <summary>
/// Pipeline node that captures the current data context value at the configured path
/// and stores it as the pipeline execution result (OutputData).
/// The result is persisted on the PipelineExecution entity when the execution completes.
/// Only pipelines that include this node will have OutputData stored — this avoids
/// unintended storage of large results from high-frequency pipelines.
/// </summary>
[NodeConfiguration(typeof(SetPipelineExecutionResultNodeConfiguration))]
public class SetPipelineExecutionResultNode(NodeDelegate next, IEtlContext etlContext) : IPipelineNode
{
    /// <summary>
    /// Key used to store the execution result in <see cref="IEtlContext.Properties"/>.
    /// </summary>
    public const string ExecutionResultPropertyKey = "PipelineExecutionResult";

    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var config = nodeContext.GetNodeConfiguration<SetPipelineExecutionResultNodeConfiguration>();

        var value = dataContext.Get<JsonNode>(config.Path);
        if (value == null)
        {
            nodeContext.Info("No data found at path {0}, skipping execution result", config.Path);
            await next(dataContext, nodeContext);
            return;
        }

        var serialized = JsonSerializer.Serialize(value, SystemTextJsonOptions.Default);

        if (serialized.Length > config.MaxLength)
        {
            nodeContext.Warning("Execution result truncated from {0} to {1} characters",
                serialized.Length, config.MaxLength);
            serialized = serialized.Substring(0, config.MaxLength);
        }

        etlContext.Properties[ExecutionResultPropertyKey] = serialized;

        nodeContext.Info("Pipeline execution result set ({0} characters)", serialized.Length);

        await next(dataContext, nodeContext);
    }
}
