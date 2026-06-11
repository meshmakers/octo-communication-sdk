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
}