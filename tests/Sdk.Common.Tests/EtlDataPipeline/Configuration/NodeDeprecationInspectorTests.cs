using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration;

#region Test configuration types

[NodeName("InspectorDeprecatedTrigger", 1)]
[NodeDeprecated("Use InspectorDeprecatedTrigger@2 instead")]
public record InspectorDeprecatedTriggerConfiguration : TriggerNodeConfiguration;

[NodeName("InspectorActiveTrigger", 1)]
public record InspectorActiveTriggerConfiguration : TriggerNodeConfiguration;

[NodeName("InspectorDeprecatedTransform", 1)]
[NodeDeprecated]
public record InspectorDeprecatedTransformConfiguration : NodeConfiguration;

[NodeName("InspectorActiveTransform", 1)]
public record InspectorActiveTransformConfiguration : NodeConfiguration;

[NodeName("InspectorGroup", 1)]
public record InspectorGroupConfiguration : NodeConfiguration, IChildNodeConfiguration
{
    public ICollection<NodeConfiguration>? Transformations { get; set; }
}

#endregion

public class NodeDeprecationInspectorTests
{
    [Fact]
    public void FindDeprecatedNodes_NoDeprecatedNodes_ReturnsEmpty()
    {
        var root = new NodeDefinitionRoot
        {
            Triggers = [new InspectorActiveTriggerConfiguration()],
            Transformations = [new InspectorActiveTransformConfiguration()]
        };

        var result = NodeDeprecationInspector.FindDeprecatedNodes(root);

        Assert.Empty(result);
    }

    [Fact]
    public void FindDeprecatedNodes_DeprecatedTrigger_IsReportedWithMessage()
    {
        var root = new NodeDefinitionRoot
        {
            Triggers = [new InspectorDeprecatedTriggerConfiguration()],
            Transformations = [new InspectorActiveTransformConfiguration()]
        };

        var result = NodeDeprecationInspector.FindDeprecatedNodes(root);

        var usage = Assert.Single(result);
        Assert.Equal("InspectorDeprecatedTrigger@1", usage.QualifiedName);
        Assert.Equal("Use InspectorDeprecatedTrigger@2 instead", usage.Message);
    }

    [Fact]
    public void FindDeprecatedNodes_DeprecatedTransform_IsReportedWithoutMessage()
    {
        var root = new NodeDefinitionRoot
        {
            Transformations = [new InspectorDeprecatedTransformConfiguration()]
        };

        var result = NodeDeprecationInspector.FindDeprecatedNodes(root);

        var usage = Assert.Single(result);
        Assert.Equal("InspectorDeprecatedTransform@1", usage.QualifiedName);
        Assert.Null(usage.Message);
    }

    [Fact]
    public void FindDeprecatedNodes_NestedInChildTransformations_IsFound()
    {
        var root = new NodeDefinitionRoot
        {
            Transformations =
            [
                new InspectorGroupConfiguration
                {
                    Transformations =
                    [
                        new InspectorGroupConfiguration
                        {
                            Transformations = [new InspectorDeprecatedTransformConfiguration()]
                        }
                    ]
                }
            ]
        };

        var result = NodeDeprecationInspector.FindDeprecatedNodes(root);

        var usage = Assert.Single(result);
        Assert.Equal("InspectorDeprecatedTransform@1", usage.QualifiedName);
    }

    [Fact]
    public void FindDeprecatedNodes_NestedInSwitchCasesAndDefault_IsFound()
    {
        var root = new NodeDefinitionRoot
        {
            Transformations =
            [
                new SwitchNodeConfiguration
                {
                    Path = "$.value",
                    Cases =
                    [
                        new SwitchCase
                        {
                            Value = "a",
                            Transformations = [new InspectorDeprecatedTransformConfiguration()]
                        }
                    ],
                    Default = [new InspectorDeprecatedTriggerConfiguration()]
                }
            ]
        };

        var result = NodeDeprecationInspector.FindDeprecatedNodes(root);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, u => u.QualifiedName == "InspectorDeprecatedTransform@1");
        Assert.Contains(result, u => u.QualifiedName == "InspectorDeprecatedTrigger@1");
    }

    [Fact]
    public void FindDeprecatedNodes_MultipleUsagesOfSameType_AreDeduplicated()
    {
        var root = new NodeDefinitionRoot
        {
            Transformations =
            [
                new InspectorDeprecatedTransformConfiguration(),
                new InspectorDeprecatedTransformConfiguration(),
                new InspectorActiveTransformConfiguration()
            ]
        };

        var result = NodeDeprecationInspector.FindDeprecatedNodes(root);

        Assert.Single(result);
    }
}
