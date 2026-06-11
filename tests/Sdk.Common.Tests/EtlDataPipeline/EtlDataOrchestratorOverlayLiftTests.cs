using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline;

/// <summary>
/// Regression tests for the terminal-delegate overlay-lift bug in <see cref="EtlDataOrchestrator"/>.
/// Spec §5.1: overlay memory cost must be bounded by what is actually written. The terminal
/// delegate previously did <c>dataContext.Set("$", ds.Get&lt;JsonNode&gt;("$"))</c> unconditionally,
/// which lifted the entire base document into the overlay even when no node had written
/// anything, defeating every <c>HasWrites</c> fast path.
/// </summary>
public class EtlDataOrchestratorOverlayLiftTests
{
    private static DefaultEtlContext MakeEtlContext() =>
        new("test", OctoObjectId.GenerateNewId(), Guid.NewGuid(),
            new RtEntityId("System.Communication/Adapter", OctoObjectId.GenerateNewId()),
            DateTime.UtcNow, null,
            new GlobalConfiguration(new List<ConfigurationDto>()),
            new Dictionary<string, object?>());

    [Fact]
    public async Task TerminalDelegate_DoesNotLiftOverlayWhenNoNodeWrote()
    {
        // Arrange: a one-node pipeline whose node writes nothing. The probe node simply
        // captures the IDataContext reference it was given so we can observe overlay state
        // after the orchestrator's terminal delegate has run.
        var services = new ServiceCollection();
        services.AddDataPipeline()
            .RegisterNode<OverlayProbeNode>();

        var captured = new CapturedDataContext();
        services.AddSingleton(captured);

        var serviceProvider = services.BuildServiceProvider();
        var orchestrator = new EtlDataOrchestrator(serviceProvider,
            serviceProvider.GetRequiredService<INodeLookupService>());

        var pipeline = new NodeDefinitionRoot
        {
            Transformations = new List<NodeConfiguration> { new OverlayProbeNodeConfiguration() }
        };

        var input = JsonNode.Parse("{\"a\":1,\"b\":{\"c\":2}}");

        // Act
        var result = await orchestrator.ExecutePipelineAsync(pipeline, MakeEtlContext(), value: input);

        // Assert: orchestrator returned the correct shape.
        Assert.NotNull(result);
        var resultNode = Assert.IsAssignableFrom<JsonNode>(result);
        Assert.Equal(1, resultNode["a"]!.GetValue<int>());

        // Critical: the dataContext we captured during node execution must be the same
        // instance the terminal delegate ran against (sanity), and its overlay must NOT
        // have been lifted just by virtue of the terminal mirror running. With the
        // pre-fix code, the terminal does Set("$", Get<JsonNode>("$")) which lifts the
        // entire base document into the overlay even though the node wrote nothing.
        Assert.NotNull(captured.DataContext);
        var impl = Assert.IsType<DataContextImpl>(captured.DataContext);
        Assert.False(impl.OverlayHasWrites,
            "Empty-write pipeline must leave the outer dataContext overlay un-lifted; " +
            "the orchestrator's terminal delegate was lifting it via Set(\"$\", Get<JsonNode>(\"$\")).");
    }

    /// <summary>Captures the most recent <see cref="IDataContext"/> handed to the probe node.</summary>
    internal sealed class CapturedDataContext
    {
        public IDataContext? DataContext { get; set; }
    }

    [NodeName("OverlayProbe", 1)]
    internal record OverlayProbeNodeConfiguration : NodeConfiguration;

    [NodeConfiguration(typeof(OverlayProbeNodeConfiguration))]
    internal sealed class OverlayProbeNode(NodeDelegate next, CapturedDataContext capture) : IPipelineNode
    {
        public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
        {
            // Capture the data context reference. The orchestrator's terminal delegate
            // runs after `next` here, so we can inspect overlay state after the pipeline
            // completes (the captured reference outlives Dispose, which only releases
            // the owned JsonDocument and leaves the overlay state observable).
            capture.DataContext = dataContext;
            await next(dataContext, nodeContext);
        }
    }
}
