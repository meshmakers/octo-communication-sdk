using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.Tests.TestData;

[NodeName("Test", 1)]
internal record TestNodeConfiguration : TargetPathNodeConfiguration;

[NodeConfiguration(typeof(TestNodeConfiguration))]
internal class TestNode(NodeDelegate next, ITestCounter testCounter) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<TestNodeConfiguration>();

        dataContext.Set(c.TargetPath, testCounter.GetNext(), c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }
}
