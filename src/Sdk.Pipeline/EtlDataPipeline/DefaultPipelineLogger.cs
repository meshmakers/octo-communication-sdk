using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
// ReSharper disable TemplateIsNotCompileTimeConstantProblem

internal class DefaultPipelineLogger(ILogger<DefaultPipelineLogger> logger) : IPipelineLogger
{
    public virtual void Debug(string nodeId, string nodePath, string message, params object[] args)
    {
        var nodePathString = string.IsNullOrWhiteSpace(nodePath) ? "" : $"{nodePath}: ";
        logger.Log(LogLevel.Debug, $"{nodePathString}{message}", args);
    }

    public virtual void Info(string nodeId, string nodePath, string message, params object[] args)
    {
        var nodePathString = string.IsNullOrWhiteSpace(nodePath) ? "" : $"{nodePath}: ";
        logger.Log(LogLevel.Information, $"{nodePathString}{message}", args);
    }

    public virtual void Warning(string nodeId, string nodePath, string message, params object[] args)
    {
        var nodePathString = string.IsNullOrWhiteSpace(nodePath) ? "" : $"{nodePath}: ";
        logger.Log(LogLevel.Warning, $"{nodePathString}{message}", args);
    }

    public virtual void Error(string nodeId, string nodePath, string message, params object[] args)
    {
        var nodePathString = string.IsNullOrWhiteSpace(nodePath) ? "" : $"{nodePath}: ";
        logger.Log(LogLevel.Error, $"{nodePathString}{message}", args);
    }

    public virtual void Error(string nodeId, string nodePath, Exception exception, string message, params object[] args)
    {
        var nodePathString = string.IsNullOrWhiteSpace(nodePath) ? "" : $"{nodePath}: ";
        logger.Log(LogLevel.Error, exception, $"{nodePathString}{message}", args);
    }

    /// <summary>
    /// Logs <paramref name="message" /> as a value with structured attributes (AB#9335). The body is
    /// <c>&lt;nodePath&gt;: &lt;message&gt;</c> like every other pipeline log line, but it reaches the
    /// logger as the formatted text of a key/value state without a <c>{OriginalFormat}</c> entry, so
    /// no provider parses it as a message template and braces stay verbatim. The attributes become the
    /// record's structured properties.
    /// </summary>
    public virtual void Log(LogLevel logLevel, string nodeId, string nodePath, string message,
        IReadOnlyList<KeyValuePair<string, object?>> attributes)
    {
        var nodePathString = string.IsNullOrWhiteSpace(nodePath) ? "" : $"{nodePath}: ";
        var state = new PipelineLogRecordState(nodePathString + message, attributes);
        logger.Log(PipelineLogRecordState.Normalize(logLevel), default, state, null,
            static (s, _) => s.Body);
    }
}