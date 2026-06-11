using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Configuration for a linear scaler node.
/// </summary>
[NodeName("LinearScaler", 1)]
public record LinearScalerNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Input signal minimum value.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public double ScaleInputMin { get; set; } = -1000000;

    /// <summary>
    /// Input signal maximum value.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public double ScaleInputMax { get; set; } =  1000000;

    /// <summary>
    /// Output signal minimum value.
    /// </summary>
    [PropertyGroup("Options", 2)]
    public double ScaleOutputMin { get; set; }= -1000000;

    /// <summary>
    /// Output signal maximum value.
    /// </summary>
    [PropertyGroup("Options", 3)]
    public double ScaleOutputMax { get; set; } =  1000000;
}

/// <summary>
/// Scales a signal linearly.
/// </summary>
[NodeConfiguration(typeof(LinearScalerNodeConfiguration))]
public class LinearScalerNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<LinearScalerNodeConfiguration>();

        var scale = (c.ScaleOutputMax - c.ScaleOutputMin) / (c.ScaleInputMax - c.ScaleInputMin);

        var value = dataContext.Get<double>(c.Path);
        var scaledValue = c.ScaleOutputMin + (value - c.ScaleInputMin) * scale;

        dataContext.Set(c.TargetPath, scaledValue, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);
        await next(dataContext, nodeContext);
    }
}
