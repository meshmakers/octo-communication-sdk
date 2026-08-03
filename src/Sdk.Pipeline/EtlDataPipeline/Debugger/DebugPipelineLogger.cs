using System.Collections.Concurrent;
using System.Threading;
using MassTransit.Monitoring.Performance;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

internal class DebugPipelineLogger(ILoggerFactory loggerFactory)
    : DefaultPipelineLogger(loggerFactory.CreateLogger<DefaultPipelineLogger>())
{
    /// <summary>
    /// Upper bound on retained debug messages per execution. Every node context in every loop
    /// ITERATION emits at least two Debug lines ("Forward Executing" / "Reverse completed"), so
    /// on large nested-ForEach runs this queue grew without bound alongside the snapshot map and
    /// contributed to the adapter OOM (AB#4662). The earliest messages are kept; the real logger
    /// (base call) is unaffected by the cap. Internal-settable for tests.
    /// </summary>
    internal int MaxRetainedMessages { get; set; } = 50_000;

    private long _retainedCount;
    private long _droppedCount;

    public ConcurrentQueue<DebugMessage> Messages { get; } = new();

    /// <summary>Number of messages dropped because <see cref="MaxRetainedMessages"/> was reached.</summary>
    internal long DroppedMessageCount => Interlocked.Read(ref _droppedCount);

    public void Clear()
    {
#if !NETSTANDARD2_0
        Messages.Clear();
#else
        while (Messages.TryDequeue(out _))
        {
        }
#endif
        Interlocked.Exchange(ref _retainedCount, 0);
        Interlocked.Exchange(ref _droppedCount, 0);
    }

    private void EnqueueBounded(DebugMessage message)
    {
        if (Interlocked.Increment(ref _retainedCount) <= MaxRetainedMessages)
        {
            Messages.Enqueue(message);
            return;
        }

        Interlocked.Decrement(ref _retainedCount);
        Interlocked.Increment(ref _droppedCount);
    }

    public override void Debug(string nodeId, string nodePath, string message, params object[] args)
    {
        base.Debug(nodeId, nodePath, message, args);
        EnqueueBounded(new DebugMessage(LoggerSeverity.Debug, nodeId, nodePath, GetMessage(message, args),
            DateTime.UtcNow));
    }

    public override void Info(string nodeId, string nodePath, string message, params object[] args)
    {
        base.Info(nodeId, nodePath, message, args);
        EnqueueBounded(new DebugMessage(LoggerSeverity.Information, nodeId, nodePath, GetMessage(message, args),
            DateTime.UtcNow));
    }

    public override void Warning(string nodeId, string nodePath, string message, params object[] args)
    {
        base.Warning(nodeId, nodePath, message, args);
        EnqueueBounded(new DebugMessage(LoggerSeverity.Warning, nodeId, nodePath, GetMessage(message, args),
            DateTime.UtcNow));
    }

    public override void Error(string nodeId, string nodePath, string message, params object[] args)
    {
        base.Error(nodeId, nodePath, message, args);
        EnqueueBounded(new DebugMessage(LoggerSeverity.Error, nodeId, nodePath, GetMessage(message, args),
            DateTime.UtcNow));
    }

    public override void Error(string nodeId, string nodePath, Exception exception, string message,
        params object[] args)
    {
        base.Error(nodeId, nodePath, exception, message, args);

        string exceptionMessage = exception.GetDirectAndIndirectMessages();
        EnqueueBounded(new DebugMessage(LoggerSeverity.Error, nodeId, nodePath, GetMessage(message, args),
            DateTime.Now, exceptionMessage));
    }

    private static string GetMessage(string message, object[] args)
    {
        return args.Length == 0 ? message : string.Format(message, args);
    }
}
