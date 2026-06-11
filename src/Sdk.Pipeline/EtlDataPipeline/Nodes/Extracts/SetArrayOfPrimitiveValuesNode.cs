using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;

/// <summary>
/// Node configuration to set an array of primitive values on the current object
/// </summary>
[NodeName("SetArrayOfPrimitiveValues", 1)]
// ReSharper disable once ClassNeverInstantiated.Global
public record SetArrayOfPrimitiveValuesNodeConfiguration : TargetPathNodeConfiguration
{
    /// <summary>
    /// The array of primitive value to set
    /// </summary>
    [PropertyGroup("Data", 0)]
    public required IEnumerable<object> Values { get; init; } = null!;
}

/// <summary>
/// Sets an array of primitive values on the current object
/// </summary>
/// <param name="next">Next node in the pipeline</param>
[NodeConfiguration(typeof(SetArrayOfPrimitiveValuesNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
public class SetArrayOfPrimitiveValuesNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<SetArrayOfPrimitiveValuesNodeConfiguration>();

        dataContext.Set(c.TargetPath, c.Values, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        return next(dataContext, nodeContext);
    }
}
