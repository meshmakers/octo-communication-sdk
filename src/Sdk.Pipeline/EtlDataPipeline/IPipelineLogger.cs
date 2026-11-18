using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// The pipeline logger that is used to log node and execution messages
/// </summary>
public interface IPipelineLogger
{
    /// <summary>
    /// Logs a message with debug severity
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="nodePath">The path to the node</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Arguments for a logging message</param>
    void Debug(string nodeId, string nodePath, string message, params object[] args);
    
    /// <summary>
    /// Logs a message with information severity
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="nodePath">The path to the node</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Arguments for a logging message</param>
    void Info(string nodeId, string nodePath, string message, params object[] args);
    
    /// <summary>
    /// Logs a message with warning severity
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="nodePath">The path to the node</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Arguments for the logging message</param>
    void Warning(string nodeId, string nodePath, string message, params object[] args);
    
    /// <summary>
    /// Logs a message with error severity
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="nodePath">The path to the node</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Arguments for a logging message</param>
    void Error(string nodeId, string nodePath, string message, params object[] args);

    /// <summary>
    /// Logs an exception with error severity
    /// </summary>
    /// <param name="nodeId">The node ID</param>
    /// <param name="nodePath">The path to the node</param>
    /// <param name="exception">The exception</param>
    /// <param name="message">The message to log</param>
    /// <param name="args">Arguments for a logging message</param>
    void Error(string nodeId, string nodePath, Exception exception, string message, params object[] args);

    /// <summary>
    /// Logs a message <b>as a value</b> together with structured attributes (AB#9335, used by
    /// <c>Logger@1</c>).
    /// </summary>
    /// <remarks>
    /// Unlike the other members, <paramref name="message" /> is never treated as a format template:
    /// braces such as <c>{0}</c> or <c>{name}</c> are logged verbatim. Every attribute is emitted as a
    /// structured property of the log record (an NLog event property, i.e. a JSON field of the
    /// adapter's log line), so Dash0 can filter and group by it.
    /// <para>
    /// A default interface member, so implementations outside this SDK keep compiling. The default
    /// forwards the message without arguments to the level's method (which logs it verbatim in the
    /// SDK's loggers) and drops the attributes; override it to keep them.
    /// </para>
    /// </remarks>
    /// <param name="logLevel">The severity; Trace is logged as Debug, Critical as Error, None as Information</param>
    /// <param name="nodeId">The node ID</param>
    /// <param name="nodePath">The path to the node</param>
    /// <param name="message">The message, logged verbatim</param>
    /// <param name="attributes">Structured attributes of the record, in the order they are given</param>
    void Log(LogLevel logLevel, string nodeId, string nodePath, string message,
        IReadOnlyList<KeyValuePair<string, object?>> attributes)
    {
        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                Debug(nodeId, nodePath, message);
                break;
            case LogLevel.Warning:
                Warning(nodeId, nodePath, message);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                Error(nodeId, nodePath, message);
                break;
            default:
                Info(nodeId, nodePath, message);
                break;
        }
    }
}