using MassTransit;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// Handles exceptions thrown by the pipeline debugger
/// </summary>
public class PipelineDebuggerException : Exception
{
    private PipelineDebuggerException()
    {
    }

    private PipelineDebuggerException(string message) : base(message)
    {
    }

    private PipelineDebuggerException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception PipelineRtEntityIdNotSet()
    {
        throw new PipelineDebuggerException("Pipeline RtEntityId not set");
    }

    internal static Exception PipelineExecutionIdNotSet()
    {
        throw new PipelineDebuggerException("Pipeline ExecutionId not set");
    }

    internal static Exception DebugPointNotFound(NodePath path)
    {
        throw new PipelineDebuggerException($"Debug point not found for path {path}");
    }
}
