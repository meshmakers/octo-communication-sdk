using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538 review: a node error that quotes a value registered as secret (a conversion error on a
/// revealed password, a URL carrying an API key) must not carry the plaintext into the execution log
/// or into the error message the adapter persists on the pipeline execution
/// (<c>ReportExecutionEndAsync(Failed, ex.Message)</c>).
/// </summary>
public class SecretExceptionRedactionTests
{
    // Obviously fake test value, never a real credential.
    private const string Secret = "fake-revealed-password-42";

    [Fact]
    public void RedactException_WithoutAMatch_ReturnsTheSameInstance()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);
        var exception = new InvalidOperationException("nothing secret here");

        Assert.Same(exception, registry.RedactException(exception));
    }

    [Fact]
    public void RedactException_WithoutRegisteredValues_ReturnsTheSameInstance()
    {
        var exception = new InvalidOperationException($"value '{Secret}'");

        Assert.Same(exception, new PipelineSecretRegistry().RedactException(exception));
    }

    [Fact]
    public void RedactException_MasksTheWholeChain_AndDropsTheOriginal()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);
        var original = new InvalidOperationException("outer failed",
            new FormatException($"'{Secret}' is not a number"));

        var redacted = registry.RedactException(original);

        Assert.NotSame(original, redacted);
        Assert.IsType<PipelineExecutionException>(redacted);
        Assert.Equal("outer failed", redacted.Message);
        Assert.Equal("'***' is not a number", redacted.InnerException!.Message);
        Assert.DoesNotContain(Secret, redacted.ToString());
    }

    [Fact]
    public void RedactException_KeepsADataPipelineExceptionADataPipelineException()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);
        var original = Assert.IsAssignableFrom<DataPipelineException>(
            DataPipelineException.TargetPipelineFailed($"bad value {Secret}"));

        var redacted = registry.RedactException(original);

        Assert.IsAssignableFrom<DataPipelineException>(redacted);
        Assert.Equal("Target pipeline failed: bad value ***", redacted.Message);
    }

    [Fact]
    public async Task Orchestrator_NodeErrorQuotingASecret_IsMaskedInTheThrownExceptionAndTheLog()
    {
        var logger = A.Fake<IPipelineLogger>();
        var (orchestrator, pipeline) = Build(logger, new ThrowingNodeConfiguration { Kind = "plain" });

        var e = await Assert.ThrowsAnyAsync<Exception>(() =>
            orchestrator.ExecutePipelineAsync(pipeline, MakeEtlContext()));

        // What AdapterTriggerContext.EndExecutePipelineAsync reports as the execution's error message.
        Assert.DoesNotContain(Secret, e.ToString());
        Assert.Contains("Error in node", e.Message);
        Assert.Contains("'***'", e.Message);
        AssertLoggedExceptionsDoNotContainTheSecret(logger);
    }

    [Fact]
    public async Task Orchestrator_DataPipelineExceptionQuotingASecret_IsMasked()
    {
        var logger = A.Fake<IPipelineLogger>();
        var (orchestrator, pipeline) = Build(logger, new ThrowingNodeConfiguration { Kind = "pipeline" });

        var e = await Assert.ThrowsAnyAsync<DataPipelineException>(() =>
            orchestrator.ExecutePipelineAsync(pipeline, MakeEtlContext()));

        Assert.DoesNotContain(Secret, e.ToString());
        Assert.Contains("***", e.Message);
        AssertLoggedExceptionsDoNotContainTheSecret(logger);
    }

    [Fact]
    public async Task Orchestrator_ErrorWithoutASecret_IsPassedThroughUnchanged()
    {
        var logger = A.Fake<IPipelineLogger>();
        var (orchestrator, pipeline) = Build(logger, new ThrowingNodeConfiguration { Kind = "harmless" });

        var e = await Assert.ThrowsAnyAsync<Exception>(() =>
            orchestrator.ExecutePipelineAsync(pipeline, MakeEtlContext()));

        Assert.Contains("harmless failure", e.Message);
        Assert.IsType<InvalidOperationException>(e.InnerException);
    }

    private static void AssertLoggedExceptionsDoNotContainTheSecret(IPipelineLogger logger)
    {
        var errorCalls = Fake.GetCalls(logger)
            .Where(c => c.Method.Name == nameof(IPipelineLogger.Error) && c.Arguments.Count == 5)
            .ToList();
        Assert.NotEmpty(errorCalls);
        foreach (var call in errorCalls)
        {
            var exception = Assert.IsAssignableFrom<Exception>(call.Arguments[2]);
            Assert.DoesNotContain(Secret, exception.ToString());
        }
    }

    private static (EtlDataOrchestrator, NodeDefinitionRoot) Build(IPipelineLogger logger,
        ThrowingNodeConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddDataPipeline().RegisterNode<ThrowingNode>();
        services.AddSingleton(logger);
        var serviceProvider = services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());
        var pipeline = new NodeDefinitionRoot { Transformations = new List<NodeConfiguration> { configuration } };
        return (orchestrator, pipeline);
    }

    private static DefaultEtlContext MakeEtlContext() =>
        new("test", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
            new RtEntityId("System.Communication/Adapter", OctoObjectId.GenerateNewId()),
            DateTime.UtcNow, null,
            new GlobalConfiguration(new List<ConfigurationDto>()),
            new Dictionary<string, object?>());

    [NodeName("SecretThrowingProbe", 1)]
    internal record ThrowingNodeConfiguration : NodeConfiguration
    {
        public string Kind { get; init; } = "plain";
    }

    /// <summary>Registers the secret (as RevealSecret@1 would) and then fails quoting it.</summary>
    [NodeConfiguration(typeof(ThrowingNodeConfiguration))]
    internal sealed class ThrowingNode : IPipelineNode
    {
        // The node lookup activates nodes with the next delegate; this one never calls it.
        public ThrowingNode(NodeDelegate next)
        {
            _ = next;
        }

        public Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
        {
            var c = nodeContext.GetNodeConfiguration<ThrowingNodeConfiguration>();
            nodeContext.RegisterSecret(Secret);
            throw c.Kind switch
            {
                "pipeline" => DataPipelineException.TargetPipelineFailed($"rejected '{Secret}'"),
                "harmless" => new InvalidOperationException("harmless failure"),
                _ => new InvalidOperationException($"cannot convert '{Secret}' to Int32")
            };
        }
    }
}
