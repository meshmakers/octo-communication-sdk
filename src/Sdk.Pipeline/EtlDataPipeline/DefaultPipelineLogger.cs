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
}