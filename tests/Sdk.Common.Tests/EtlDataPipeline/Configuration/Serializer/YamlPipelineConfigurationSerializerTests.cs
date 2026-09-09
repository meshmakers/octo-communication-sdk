using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
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

    [Theory]
    [InlineData("Fail")]
    [InlineData("FAIL")]
    [InlineData("fail")]
    public async Task Deserialize_JoinOptions_OK(string noMatchHandling)
    {
        // The Join@1 options are reachable from pipeline YAML under their camelCase names; the
        // enum value is matched case-insensitively.
        var yaml = $"""
            transformations:
              - type: Join@1
                path: $.orders[*]
                keyPath: $.orderId
                joinPath: $.orderItems[*]
                joinKeyPath: $.orderId
                itemPath: $.items
                allowMissingKey: true
                noMatchHandling: {noMatchHandling}
            """;

        var serviceProvider = dataPipelineFixture.Services.BuildServiceProvider();
        var nodeQualifiedNameLookupService = serviceProvider.GetRequiredService<INodeQualifiedNameLookupService>();
        var serializer = new YamlPipelineConfigurationSerializer(nodeQualifiedNameLookupService);

        var root = await serializer.DeserializeAsync(yaml);

        var join = Assert.IsType<JoinNodeConfiguration>(Assert.Single(root.Transformations!));
        Assert.True(join.AllowMissingKey);
        Assert.Equal(JoinNoMatchHandlingDto.Fail, join.NoMatchHandling);
    }

    [Fact]
    public async Task Deserialize_JoinWithoutOptions_UsesDefaults()
    {
        // A Join@1 written before the options existed keeps its behavior: both options resolve
        // to their defaults.
        const string yaml = """
            transformations:
              - type: Join@1
                path: $.orders[*]
                keyPath: $.orderId
                joinPath: $.orderItems[*]
                joinKeyPath: $.orderId
                itemPath: $.items
            """;

        var serviceProvider = dataPipelineFixture.Services.BuildServiceProvider();
        var nodeQualifiedNameLookupService = serviceProvider.GetRequiredService<INodeQualifiedNameLookupService>();
        var serializer = new YamlPipelineConfigurationSerializer(nodeQualifiedNameLookupService);

        var root = await serializer.DeserializeAsync(yaml);

        var join = Assert.IsType<JoinNodeConfiguration>(Assert.Single(root.Transformations!));
        Assert.False(join.AllowMissingKey);
        Assert.Equal(JoinNoMatchHandlingDto.Ignore, join.NoMatchHandling);
    }
}
