using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538: values registered as secret in an execution never reach debug snapshots, dry-run intents,
/// the execution log or the persisted execution result.
/// </summary>
public class SecretRedactionTests(ServiceCollectionFixture fixture) : IClassFixture<ServiceCollectionFixture>
{
    // Obviously fake test value, never a real credential.
    private const string Secret = "fake-revealed-password-42";

    private static DefaultPipelineDebugger NewDebugger()
    {
        var debugger = new DefaultPipelineDebugger(NullLoggerFactory.Instance);
        debugger.RegisterPipelineRtEntityId(new RtEntityId("Test/Pipeline", OctoObjectId.GenerateNewId()),
            Guid.NewGuid());
        return debugger;
    }

    [Fact]
    public void ChildNodeContexts_ShareTheRootRegistry()
    {
        using var dataContext = new DataContextImpl();
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext);
        var child = root.RegisterChildNode("Child", 0, new LoggerNodeConfiguration { Message = "m" }, dataContext);
        var grandChild = child.RegisterChildNode(1, new LoggerNodeConfiguration { Message = "m" }, dataContext);

        grandChild.RegisterSecret(Secret);

        Assert.Same(root.SecretRegistry, ((INodeContext)grandChild).SecretRegistry);
        Assert.True(root.SecretRegistry.HasSecrets);
    }

    [Fact]
    public void NewRootNodeContext_StartsWithAnEmptyRegistry()
    {
        using var dataContext = new DataContextImpl();
        var first = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext);
        first.RegisterSecret(Secret);

        var second = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext);

        Assert.NotSame(first.SecretRegistry, second.SecretRegistry);
        Assert.False(second.SecretRegistry.HasSecrets);
    }

    [Fact]
    public void DebugSnapshot_AfterRegistration_MasksTheValue()
    {
        var debugger = NewDebugger();
        using var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext, debugger);
        var node = root.RegisterChildNode("RevealSecret@1", 0, new LoggerNodeConfiguration { Message = "m" },
            dataContext);

        // What RevealSecret@1 does: write the plaintext, register it.
        dataContext.Set("$.credentials.password", Secret);
        dataContext.Set("$.header", $"Basic user:{Secret}");
        node.RegisterSecret(Secret);
        node.Unregister(dataContext);

        var info = debugger.GetDebugInformation();
        var output = info.DebugPoints.Single(p => p.Output != null).Output!;
        Assert.DoesNotContain(Secret, output);
        var json = JsonNode.Parse(output)!;
        Assert.Equal("***", json["credentials"]!["password"]!.GetValue<string>());
        Assert.Equal("Basic user:***", json["header"]!.GetValue<string>());

        // The data itself is untouched - downstream nodes still read the plaintext.
        Assert.Equal(Secret, dataContext.Get<string>("$.credentials.password"));
    }

    [Fact]
    public void DryRunIntent_MasksRegisteredValues()
    {
        var debugger = NewDebugger();
        using var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext, debugger);
        var node = root.RegisterChildNode("ApplyChanges@2", 0, new LoggerNodeConfiguration { Message = "m" },
            dataContext);

        node.RegisterSecret(Secret);
        node.RecordDryRunIntent("ApplyChanges@2", new { refreshToken = Secret });

        var point = debugger.GetDebugInformation().DebugPoints.Single(p => p.DryRunIntent != null);
        Assert.NotNull(point.DryRunIntent);
        Assert.DoesNotContain(Secret, point.DryRunIntent);
        Assert.Contains("***", point.DryRunIntent);
    }

    [Fact]
    public void Debugger_WithSeveralRegistries_MasksTheValuesOfAll()
    {
        var debugger = NewDebugger();
        var first = new PipelineSecretRegistry();
        var second = new PipelineSecretRegistry();
        debugger.AddSecretRegistry(first);
        debugger.AddSecretRegistry(second);
        first.Register("fake-first-secret");
        second.Register("fake-second-secret");

        debugger.LogOutput("0:n", new NodePath("n"), null, 0,
            JsonNode.Parse("""{"a":"fake-first-secret","b":"fake-second-secret"}"""));

        var output = debugger.GetDebugInformation().DebugPoints.Single().Output!;
        Assert.Equal("""{"a":"***","b":"***"}""", output);
    }

    [Fact]
    public void ExecutionLog_MasksMessageAndArguments()
    {
        var logger = A.Fake<IPipelineLogger>();
        using var dataContext = new DataContextImpl();
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        root.RegisterSecret(Secret);

        root.Info($"token is {Secret}");
        root.Warning("value {0}", Secret);

        A.CallTo(() => logger.Info(A<string>._, A<string>._, "token is ***", A<object[]>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => logger.Warning(A<string>._, A<string>._, "value {0}",
                A<object[]>.That.Matches(a => a.Length == 1 && (string)a[0] == "***")))
            .MustHaveHappenedOnceExactly();
        var leaked = Fake.GetCalls(logger).Any(call =>
            call.Arguments.Any(a => (a as string)?.Contains(Secret) == true ||
                                    (a as object[])?.Any(x => (x as string)?.Contains(Secret) == true) == true));
        Assert.False(leaked);
    }

    [Fact]
    public async Task SetPipelineExecutionResult_MasksRegisteredValues()
    {
        var properties = new Dictionary<string, object?>();
        var etlContext = A.Fake<IEtlContext>();
        A.CallTo(() => etlContext.Properties).Returns(properties);
        using var dataContext = new DataContextImpl(JsonDocument.Parse(
            $$$"""{"result":{"user":"svc","password":"{{{Secret}}}"}}"""));
        var root = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(),
            A.Fake<IPipelineLogger>(), dataContext);
        var nodeContext = root.RegisterChildNode("SetPipelineExecutionResult", 0,
            new SetPipelineExecutionResultNodeConfiguration { Path = "$.result" }, dataContext);
        nodeContext.RegisterSecret(Secret);

        await new SetPipelineExecutionResultNode(A.Fake<NodeDelegate>(), etlContext)
            .ProcessObjectAsync(dataContext, nodeContext);

        var stored = (string)properties[SetPipelineExecutionResultNode.ExecutionResultPropertyKey]!;
        Assert.DoesNotContain(Secret, stored);
        Assert.Equal("""{"user":"svc","password":"***"}""", stored);
    }
}
