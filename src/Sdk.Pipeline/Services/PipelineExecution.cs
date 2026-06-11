namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Represents the lifetime of a pipeline execution
/// </summary>
/// <param name="PipelineExecutionId">The pipeline execution id</param>
/// <param name="StartedDateTime">The date and time the pipeline execution started</param>
/// <param name="ExecutePipelineTask">The task that executes the pipeline</param>
/// <param name="Properties">Properties to store state between start- and end pipeline</param>
// ReSharper disable once NotAccessedPositionalProperty.Global
public record PipelineExecution(
    Guid PipelineExecutionId,
    DateTime StartedDateTime,
    Task<object?> ExecutePipelineTask,
    // ReSharper disable once NotAccessedPositionalProperty.Global
    Dictionary<string, object?> Properties);