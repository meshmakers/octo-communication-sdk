using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration.Serializer;

public class YamlPipelineConfigurationSerializerTests(DataPipelineFixture dataPipelineFixture) : IClassFixture<DataPipelineFixture>
{
    [Fact]
    public async Task Serialize_OK()
    {
        NodeDefinitionRoot configurationRoot = new NodeDefinitionRoot();
        configurationRoot.Transformations ??= new List<NodeConfiguration>();
        configurationRoot.Transformations.Add(new SelectByPathNodeConfiguration
        {
            SelectPath = new List<PathPropertyConfigurationNode>
            {
                new()
                {
                    TargetPath = "$.CustomerName",
                }
            }
        });

        var serviceProvider = dataPipelineFixture.Services.BuildServiceProvider();
        var nodeQualifiedNameLookupService = serviceProvider.GetRequiredService<INodeQualifiedNameLookupService>(); 

        var serializer = new YamlPipelineConfigurationSerializer(nodeQualifiedNameLookupService);
        using (var memoryStream = new MemoryStream())
        {
            var streamWriter = new StreamWriter(memoryStream);
            await serializer.SerializeAsync(streamWriter, configurationRoot);
            await streamWriter.FlushAsync(TestContext.Current.CancellationToken);

            memoryStream.Position = 0;

            using var streamReader = new StreamReader(memoryStream);
            var s = await streamReader.ReadToEndAsync(TestContext.Current.CancellationToken);

            memoryStream.Position = 0;

            var copy = await serializer.DeserializeAsync(memoryStream);
            Assert.NotNull(copy);
            Assert.Equal(1, copy.Transformations?.Count);
        }
    }
}