using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Triggers;

public class FromPollingNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    [Fact]
    public async Task StartAsync_RegistersPollingCallback_WithCorrectInterval()
    {
        var pollingService = A.Fake<IPollingService>();
        var pollingHandle = A.Fake<PollingHandle>();
        A.CallTo(() => pollingService.RegisterCallback(A<TimeSpan>._, A<Func<Task>>._))
            .Returns(pollingHandle);

        var triggerContext = CreateTriggerContext(new FromPollingNodeConfiguration
        {
            Interval = TimeSpan.FromSeconds(30)
        });

        var testee = new FromPollingNode(pollingService);

        await testee.StartAsync(triggerContext);

        A.CallTo(() => pollingService.RegisterCallback(
            TimeSpan.FromSeconds(30),
            A<Func<Task>>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StopAsync_AfterStart_UnregistersCallback()
    {
        var pollingService = A.Fake<IPollingService>();
        var pollingHandle = new PollingHandle(pollingService);
        A.CallTo(() => pollingService.RegisterCallback(A<TimeSpan>._, A<Func<Task>>._))
            .Returns(pollingHandle);

        var triggerContext = CreateTriggerContext(new FromPollingNodeConfiguration
        {
            Interval = TimeSpan.FromSeconds(10)
        });

        var testee = new FromPollingNode(pollingService);
        await testee.StartAsync(triggerContext);

        await testee.StopAsync(triggerContext);

        A.CallTo(() => pollingService.UnregisterCallback(pollingHandle))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotThrow()
    {
        var pollingService = A.Fake<IPollingService>();
        var triggerContext = CreateTriggerContext(new FromPollingNodeConfiguration
        {
            Interval = TimeSpan.FromSeconds(10)
        });

        var testee = new FromPollingNode(pollingService);

        await testee.StopAsync(triggerContext);
    }

    [Fact]
    public async Task StartAsync_WithInput_RegistersCallbackWithInput()
    {
        var pollingService = A.Fake<IPollingService>();
        var pollingHandle = A.Fake<PollingHandle>();
        A.CallTo(() => pollingService.RegisterCallback(A<TimeSpan>._, A<Func<Task>>._))
            .Returns(pollingHandle);

        var inputData = new JsonObject { ["sensor"] = "T1" };
        var triggerContext = CreateTriggerContext(new FromPollingNodeConfiguration
        {
            Interval = TimeSpan.FromMinutes(5),
            Input = inputData
        });

        var testee = new FromPollingNode(pollingService);

        await testee.StartAsync(triggerContext);

        A.CallTo(() => pollingService.RegisterCallback(
            TimeSpan.FromMinutes(5),
            A<Func<Task>>._)).MustHaveHappenedOnceExactly();
    }

    private ITriggerContext CreateTriggerContext(FromPollingNodeConfiguration config)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl();

        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("FromPolling", 0, config, dataContext);

        var triggerContext = A.Fake<ITriggerContext>();
        A.CallTo(() => triggerContext.NodeContext).Returns(nodeContext);
        A.CallTo(() => triggerContext.TenantId).Returns("test-tenant");

        return triggerContext;
    }
}
