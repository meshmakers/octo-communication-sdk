using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Sdk.Common.Tests.Fixtures;

/// <summary>
/// Base class for node unit tests providing common helpers for the new path-only
/// <see cref="IDataContext"/> surface. Tests construct a real <see cref="DataContextImpl"/>
/// from a JsonNode literal and a NodeContext to invoke <see cref="IPipelineNode.ProcessObjectAsync"/>.
/// </summary>
public abstract class NodeTestBase
{
    protected static IDataContext CreateDataContext(JsonNode? root)
    {
        var json = root?.ToJsonString() ?? "{}";
        return new DataContextImpl(JsonDocument.Parse(json));
    }

    protected static IDataContext CreateDataContext(string json)
    {
        return new DataContextImpl(JsonDocument.Parse(json));
    }

    protected static (IDataContext DataContext, INodeContext NodeContext, NodeDelegate Next) PrepareTest<TConfig>(
        TConfig config,
        JsonNode? testData = null,
        IServiceProvider? serviceProvider = null)
        where TConfig : class, INodeConfiguration
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateDataContext(testData);
        var sp = serviceProvider ?? new ServiceCollection().BuildServiceProvider();
        var rootNodeContext = NodeContext.CreateRootNodeContext(sp, logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode(
            typeof(TConfig).Name.Replace("Configuration", ""),
            0,
            config,
            dataContext);
        var next = A.Fake<NodeDelegate>();
        return (dataContext, nodeContext, next);
    }

    protected static void VerifyNextCalled(NodeDelegate next, IDataContext dataContext, INodeContext nodeContext)
    {
        A.CallTo(() => next(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    protected static void VerifyNextNotCalled(NodeDelegate next, IDataContext dataContext, INodeContext nodeContext)
    {
        A.CallTo(() => next(dataContext, nodeContext)).MustNotHaveHappened();
    }
}
