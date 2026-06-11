using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sdk.Common.Tests.TestData;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.Fixtures;

public class NodeFixture : ServiceCollectionFixture
{
    private readonly List<NodeLookup> _nodeLookups = new();

    public NodeFixture()
    {
        RegisterNode(typeof(TestNode));
        RegisterNode(typeof(TestOutputNode));
        RegisterNode(typeof(ExceptionNode));
        RegisterNode(typeof(DelayedTestNode));
        RegisterNode(typeof(FullDocAccessTestNode));
        Services.TryAddSingleton<INodeLookupService>(_ => new NodeLookupService(_nodeLookups));
    }


    public void RegisterNode(Type node)
    {
        var configurationType = node.GetNodeConfigurationType();
        var qualifiedName = configurationType.GetConfigurationQualifiedName();

        _nodeLookups.Add(new NodeLookup(qualifiedName, node, configurationType));
    }
}