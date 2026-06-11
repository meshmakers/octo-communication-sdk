using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.IntegrationTests.Fixtures;

/// <summary>
/// Fixture for integration tests that provides a fully configured service provider
/// with all built-in pipeline nodes registered.
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    private ServiceProvider? _serviceProvider;
    private readonly TestGlobalConfiguration _globalConfiguration = new();

    public IntegrationTestFixture()
    {
        Services = new ServiceCollection();

        Services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Register test global configuration
        Services.AddSingleton<IGlobalConfiguration>(_globalConfiguration);

        // Register the data pipeline with all built-in nodes
        Services.AddDataPipeline();
    }

    public ServiceCollection Services { get; }

    public ServiceProvider ServiceProvider => _serviceProvider ??= Services.BuildServiceProvider();

    public IEtlDataOrchestrator CreateOrchestrator()
    {
        return new EtlDataOrchestrator(
            ServiceProvider,
            ServiceProvider.GetRequiredService<INodeLookupService>());
    }

    public IEtlContext CreateContext(string pipelineName = "integration-test")
    {
        return new DefaultEtlContext(
            pipelineName,
            OctoObjectId.GenerateNewId(),
            Guid.NewGuid(),
            new RtEntityId("Test/Pipeline", OctoObjectId.GenerateNewId()),
            DateTime.UtcNow,
            null,
            ServiceProvider.GetRequiredService<IGlobalConfiguration>(),
            new Dictionary<string, object?>());
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
