using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration;

public class GroupNodeSchemaTests
{
    private static NodeSchemaRegistry CreateRegistry()
    {
        var lookups = new List<NodeLookup>
        {
            new("Group@1", typeof(GroupNode), typeof(GroupNodeConfiguration))
        };
        return new NodeSchemaRegistry(lookups, new Dictionary<string, Type>());
    }

    [Fact]
    public void GroupDescriptor_SupportsChildren_AndHasNodeKindHint()
    {
        var registry = CreateRegistry();
        var descriptor = registry.GetDescriptor("Group@1");

        Assert.NotNull(descriptor);
        Assert.True(descriptor!.SupportsChildren);

        var schema = JsonNode.Parse(descriptor.ConfigurationSchemaJson)!.AsObject();
        Assert.Equal("group", schema["x-nodeKind"]?.GetValue<string>());

        Assert.NotNull(schema["properties"]?["transformations"]);
    }
}
