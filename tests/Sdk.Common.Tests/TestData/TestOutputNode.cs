using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.Tests.TestData;

[NodeName("TestOutput", 1)]
internal record TestOutputNodeConfiguration : TargetPathNodeConfiguration
{
    public object? TargetValue { get; set; }
}

[NodeConfiguration(typeof(TestOutputNodeConfiguration))]
internal class TestOutputNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<TestOutputNodeConfiguration>();

        dataContext.Set(c.TargetPath, c.TargetValue, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }
}
