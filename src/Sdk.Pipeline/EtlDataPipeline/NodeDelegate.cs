using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// A function that can process a node in the pipeline
/// </summary>
/// <param name="dataContext">Context to access the current pipeline data.</param>
/// <param name="nodeContext">Context to access the current node data.</param>
public delegate Task NodeDelegate(IDataContext dataContext, INodeContext nodeContext);
