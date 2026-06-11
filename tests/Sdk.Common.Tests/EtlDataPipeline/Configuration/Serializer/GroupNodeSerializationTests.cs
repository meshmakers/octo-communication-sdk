using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration.Serializer;

public class GroupNodeSerializationTests(DataPipelineFixture fixture) : IClassFixture<DataPipelineFixture>
{
    [Fact]
    public async Task Group_With_Children_RoundTrips()
    {
        var root = new NodeDefinitionRoot { Transformations = new List<NodeConfiguration>() };
        root.Transformations.Add(new GroupNodeConfiguration
        {
            Name = "set status",
            Transformations = new List<NodeConfiguration>
            {
                new LoggerNodeConfiguration { Message = "hello" }
            }
        });

        var serviceProvider = fixture.Services.BuildServiceProvider();
        var lookup = serviceProvider.GetRequiredService<INodeQualifiedNameLookupService>();
        var serializer = new YamlPipelineConfigurationSerializer(lookup);

        using var memoryStream = new MemoryStream();
        var writer = new StreamWriter(memoryStream);
        await serializer.SerializeAsync(writer, root);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var yaml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        memoryStream.Position = 0;
        var copy = await serializer.DeserializeAsync(memoryStream);

        Assert.Contains("Group@1", yaml);
        Assert.Contains("Logger@1", yaml);
        Assert.NotNull(copy);
        var group = Assert.IsType<GroupNodeConfiguration>(copy!.Transformations!.Single());
        Assert.Equal("set status", group.Name);
        Assert.Single(group.Transformations!);
        Assert.IsType<LoggerNodeConfiguration>(group.Transformations!.Single());
    }
}
