using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;
using Sdk.Common.Tests.TestData.Dto;

namespace Sdk.Common.Tests.EtlDataPipeline;

public class EtlDataOrchestratorTests(DataPipelineFixture fixture, ITestOutputHelper testOutputHelper)
    : IClassFixture<DataPipelineFixture>
{
    [Fact]
    public async Task ExecutePipelineAsync_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var dataOrchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());

        var order = Generator.GenerateOrder();
        var r = await dataOrchestrator.ExecutePipelineAsync(TestPipelineConfigurations.Test1WithOrder(order),
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
                new RtEntityId("System.Communication/Adapter", OctoObjectId.GenerateNewId()), DateTime.UtcNow, null,
                new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>()));

        Assert.NotNull(r);

        // Transformed from InvoiceNumber, but linear scaling (0..100 -> 0..1000) was applied
        var node = Assert.IsAssignableFrom<JsonObject>(r);
        Assert.Equal(3, node.Count);
        Assert.Equal(order.InvoiceNumber * 10.0, node["InvoiceNumber"]!.GetValue<double>());

        // Transformed from Items
        var t = node["OrderItems"] as JsonArray;
        Assert.NotNull(t);
        Assert.Equal(3, t.Count);

        // This property was not excluded from source
        Assert.NotNull(node["InvoiceDate"]);
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithDebug_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var dataOrchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());

        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);

        var r = await dataOrchestrator.ExecutePipelineAsync(TestPipelineConfigurations.Test1,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), pipelineExecutionId, pipelineEntityId,
                DateTime.UtcNow, null,
                new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>()), debugger);

        Assert.NotNull(r);

        IPipelineDebugSerializer serializer = serviceProvider.GetRequiredService<IPipelineDebugSerializer>();
        var debugInfo = await serializer.SerializeAsync(debugger.GetDebugInformation());
        Assert.NotNull(debugInfo);
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithDebugWithException_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var dataOrchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());

        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);

        var ex = await Assert.ThrowsAsync<DataPipelineException>(async () => await dataOrchestrator.ExecutePipelineAsync(
            TestPipelineConfigurations.Test2,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), pipelineExecutionId, pipelineEntityId,
                DateTime.UtcNow, null,
                new GlobalConfiguration(new List<ConfigurationDto>()), new Dictionary<string, object?>()), debugger));

        Assert.IsType<MyCustomException>(ex.InnerException);
        Assert.Contains("Exception@", ex.Message);

        var debugInfo = debugger.GetDebugInformation();
        Assert.NotNull(debugInfo);

        // Verify error is logged at the specific node level
        var nodeDb = debugInfo.DebugPoints.FirstOrDefault(db => db.NodePath.ToString().Contains("Exception@"));
        Assert.NotNull(nodeDb);
        var nodeMessage = nodeDb.Messages?.FirstOrDefault(m => m.Severity == LoggerSeverity.Error);
        Assert.True(nodeMessage?.ExceptionMessage?.StartsWith("Test exception"));

        // Verify error is also logged at pipeline execution level (with wrapped message)
        var db = debugInfo.DebugPoints.FirstOrDefault(db => db.NodePath == "PipelineExecution");
        Assert.NotNull(db);
        var message = db.Messages?.FirstOrDefault(m => m.Severity == LoggerSeverity.Error);
        Assert.Contains("Error in node", message?.ExceptionMessage);
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithDebugWithOutput_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var dataOrchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());

        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);

        var result = await dataOrchestrator.ExecutePipelineAsync(TestPipelineConfigurations.TestDataSingleNode,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), pipelineExecutionId, pipelineEntityId,
                DateTime.UtcNow, null, new GlobalConfiguration(new List<ConfigurationDto>()),
                new Dictionary<string, object?>()), debugger);

        var node = Assert.IsAssignableFrom<JsonObject>(result);
        Assert.Equal("{\"TestOutput\":100}", node.ToJsonString());
        var debugInfo = debugger.GetDebugInformation();
        Assert.NotNull(debugInfo);
        var db = debugInfo.DebugPoints.FirstOrDefault(db => db.NodePath == "PipelineExecution/TestOutput@1");
        Assert.NotNull(db);
        Assert.Equal("{\"TestOutput\":100}", db.Output);
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithDebugWithOutputMultiple_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var dataOrchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());

        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);

        var result = await dataOrchestrator.ExecutePipelineAsync(TestPipelineConfigurations.TestDataMultipleNodes,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), pipelineExecutionId, pipelineEntityId,
                DateTime.UtcNow, null, new GlobalConfiguration(new List<ConfigurationDto>()),
                new Dictionary<string, object?>()), debugger);

        var node = Assert.IsAssignableFrom<JsonObject>(result);
        Assert.Equal("{\"TestOutput0\":100,\"TestOutput1\":101,\"TestOutput2\":102,\"TestOutput3\":103}",
            node.ToJsonString());
        var debugInfo = debugger.GetDebugInformation();
        Assert.NotNull(debugInfo);
        Assert.Equal(5, debugInfo.DebugPoints.Count);
        Assert.Equal("{\"TestOutput0\":100}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodeId == "0:PipelineExecution/0:TestOutput@1")?.Output);
        Assert.Equal("{\"TestOutput0\":100,\"TestOutput1\":101}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodeId == "0:PipelineExecution/1:TestOutput@1")?.Output);
        Assert.Equal("{\"TestOutput0\":100,\"TestOutput1\":101,\"TestOutput2\":102}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodeId == "0:PipelineExecution/2:TestOutput@1")?.Output);
        Assert.Equal("{\"TestOutput0\":100,\"TestOutput1\":101,\"TestOutput2\":102,\"TestOutput3\":103}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodeId == "0:PipelineExecution/3:TestOutput@1")?.Output);

        Assert.Equal("{\"TestOutput0\":100,\"TestOutput1\":101,\"TestOutput2\":102,\"TestOutput3\":103}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodeId == "0:PipelineExecution")?.Output);
    }

    [Fact]
    public async Task ExecutePipelineAsync_WithDebugWithInput_OK()
    {
        fixture.UseXUnitLoggerFactory(testOutputHelper);
        var serviceProvider = fixture.Services.BuildServiceProvider();

        var testData = Generator.GenerateSimpleData();

        var dataOrchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var debugger = new DefaultPipelineDebugger(serviceProvider.GetRequiredService<ILoggerFactory>());

        var pipelineExecutionId = Guid.NewGuid();
        var pipelineEntityId = new RtEntityId("System.Communication/Pipeline", OctoObjectId.GenerateNewId());
        debugger.RegisterPipelineRtEntityId(pipelineEntityId, pipelineExecutionId);

        var result = await dataOrchestrator.ExecutePipelineAsync(TestPipelineConfigurations.TestDataSingleNode,
            new DefaultEtlContext("test1", OctoObjectId.GenerateNewId(), pipelineExecutionId, pipelineEntityId,
                DateTime.UtcNow, null, new GlobalConfiguration(new List<ConfigurationDto>()),
                new Dictionary<string, object?>()), debugger, testData);

        var node = Assert.IsAssignableFrom<JsonObject>(result);
        Assert.Equal(100, node["TestOutput"]!.GetValue<int>());
        Assert.Equal(testData.Value, node["Value"]!.GetValue<int>());

        var debugInfo = debugger.GetDebugInformation();
        Assert.NotNull(debugInfo);
        Assert.Equal(2, debugInfo.DebugPoints.Count);

        Assert.Equal("{\"Value\":" + testData.Value + "}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodePath == "PipelineExecution")?.Input);
        Assert.Equal("{\"Value\":" + testData.Value + ",\"TestOutput\":100}",
            debugInfo.DebugPoints.FirstOrDefault(db => db.NodePath == "PipelineExecution")?.Output);
    }
}
