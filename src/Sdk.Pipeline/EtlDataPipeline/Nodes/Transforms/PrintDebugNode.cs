using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Internal;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for the PrintDebugNode
/// </summary>
[NodeName("PrintDebug", 1)]
public record PrintDebugNodeConfiguration : NodeConfiguration
{
    /// <summary>
    /// The severity of the log message.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public LoggerSeverity Severity { get; set; } = LoggerSeverity.Information;
}

/// <summary>
/// Prints the current object to the log
/// </summary>
[NodeConfiguration(typeof(PrintDebugNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class PrintDebugNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var config = nodeContext.GetNodeConfiguration<PrintDebugNodeConfiguration>();

        var rootNode = dataContext.Get<JsonNode>("$");
        // Route through JsonStringifyHelper so object/array roots render with the
        // 2-space indented format that matched legacy JObject.ToString() debug output.
        // Compact JSON in a log line is hostile to debugging.
        var message = JsonStringifyHelper.ToLegacyString(rootNode) ?? "null";

        switch (config.Severity)
        {
            case LoggerSeverity.Debug:
                nodeContext.Debug(message);
                break;
            case LoggerSeverity.Information:
                nodeContext.Info(message);
                break;
            case LoggerSeverity.Warning:
                nodeContext.Warning(message);
                break;
            case LoggerSeverity.Error:
                nodeContext.Error(message);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(config.Severity), config.Severity, null);
        }

        await next(dataContext, nodeContext);
    }
}