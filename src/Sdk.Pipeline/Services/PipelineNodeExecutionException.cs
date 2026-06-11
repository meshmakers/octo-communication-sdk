namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Exception thrown when a pipeline node execution fails
/// </summary>
public class PipelineNodeExecutionException : Exception
{
    /// <summary>
    /// Constructor
    /// </summary>
    public PipelineNodeExecutionException()
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    public PipelineNodeExecutionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    public PipelineNodeExecutionException(string message, Exception inner) : base(message, inner)
    {
    }

}