using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Base interface for all pipeline nodes.
/// </summary>
public interface IPipelineNode
{
    /// <summary>
    /// Processes an object.
    /// </summary>
    /// <param name="dataContext">Context to access the current pipeline data.</param>
    /// <param name="nodeContext">Context to access the current node data.</param>
    /// <returns></returns>
    Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext);
}