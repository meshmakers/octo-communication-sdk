using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.Tests.TestData;

[NodeName("Exception", 1)]
internal record ExceptionNodeConfiguration : TargetPathNodeConfiguration;

[NodeConfiguration(typeof(ExceptionNodeConfiguration))]
internal class ExceptionNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        throw new MyCustomException("Test exception");

#pragma warning disable CS0162 // Unreachable code detected
        await next(dataContext, nodeContext);
#pragma warning restore CS0162 // Unreachable code detected
    }
}