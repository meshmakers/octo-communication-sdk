using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sdk.Common.Tests.Fixtures;

public class ServiceCollectionFixture
{
    public ServiceCollectionFixture()
    {
        Services = new ServiceCollection();
        PipelineServices = new ServiceCollection();
        Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
    }

    public ServiceCollection Services { get; }
    public ServiceCollection PipelineServices { get; }
    
    public void UseXUnitLoggerFactory(ITestOutputHelper testOutputHelper)
    {
        Services.AddSingleton<ITestOutputHelper>(sp => testOutputHelper);
        Services.AddSingleton<ILoggerFactory, XUnitLoggerFactory>();
    }
}