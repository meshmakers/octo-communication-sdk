using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for node logger
/// </summary>
[NodeName("Logger", 1)]
public record LoggerNodeConfiguration : NodeConfiguration
{
    /// <summary>
    /// Message to log
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required string Message { get; init; } = null!;
}

/// <summary>
/// Logs a message
/// </summary>
/// <param name="next"></param>
[NodeConfiguration(typeof(LoggerNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
internal class LoggerNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<LoggerNodeConfiguration>();
        nodeContext.Info( c.Message);

        await next(dataContext, nodeContext);
    }
}