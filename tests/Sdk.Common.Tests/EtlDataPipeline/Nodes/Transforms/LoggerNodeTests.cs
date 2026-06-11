using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class LoggerNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    [Fact]
    public async Task ProcessObjectAsync_LogsMessage_CallsNext()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"value\":42}"));

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Logger", 0, new LoggerNodeConfiguration
        {
            Message = "Test log message"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new LoggerNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_WithEmptyMessage_CallsNext()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl();

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Logger", 0, new LoggerNodeConfiguration
        {
            Message = ""
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new LoggerNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_DataContextNotModified()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{\"sensor\":\"T1\",\"temperature\":23.5}"));

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Logger", 0, new LoggerNodeConfiguration
        {
            Message = "Processing sensor data"
        }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new LoggerNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        Assert.Equal("T1", dataContext.Get<string>("$.sensor"));
        Assert.Equal(23.5, dataContext.Get<double>("$.temperature"));
    }
}
