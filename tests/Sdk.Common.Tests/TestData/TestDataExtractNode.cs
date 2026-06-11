using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.Tests.TestData;

[NodeName("TestDataExtractNode", 1)]
public record TestDataExtractNodeConfiguration : NodeConfiguration
{
    public object? Data { get; set; }
}

[NodeConfiguration(typeof(TestDataExtractNodeConfiguration))]
// ReSharper disable once ClassNeverInstantiated.Global
internal class TestDataExtractNode(NodeDelegate next) : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<TestDataExtractNodeConfiguration>();

        var asNode = c.Data is null
            ? new JsonObject()
            : (JsonSerializer.SerializeToNode(c.Data) ?? new JsonObject());

        dataContext.Set("$", asNode);

        await next(dataContext, nodeContext);
    }
}
