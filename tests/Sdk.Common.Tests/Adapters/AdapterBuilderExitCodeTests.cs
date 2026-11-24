using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     The process exit code and the global NLog configuration are shared by the whole test run,
///     so these tests run alone.
/// </summary>
[CollectionDefinition(nameof(ProcessExitCodeCollection), DisableParallelization = true)]
public sealed class ProcessExitCodeCollection;

/// <summary>
///     The builder ends the process with a non-zero exit code when its host cannot be built or started,
///     so Kubernetes reports a crash instead of a clean exit.
/// </summary>
[Collection(nameof(ProcessExitCodeCollection))]
public sealed class AdapterBuilderExitCodeTests : IDisposable
{
    private readonly int _exitCodeBefore = Environment.ExitCode;
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "adapter-builder-exit-code-" + Guid.NewGuid().ToString("N"));

    public AdapterBuilderExitCodeTests()
    {
        Directory.CreateDirectory(_workDirectory);
        Environment.ExitCode = 0;
    }

    public void Dispose()
    {
        Environment.ExitCode = _exitCodeBefore;
        Directory.Delete(_workDirectory, recursive: true);
    }

    [Fact]
    public void AConfigurationThatDoesNotBindEndsTheProcessWithANonZeroExitCode()
    {
        var adapterBuilder = new AdapterBuilder();

        adapterBuilder.Run([],
            configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AdapterPoolMemberOptions.SectionName}:AdapterPoolTenantId"] = "accounting"
            }),
            (_, _) => { });

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, Environment.ExitCode);
    }

    [Fact]
    public void AStartupFailureAfterTheNLogReloadIsLoggedOnceAndEndsWithANonZeroExitCode()
    {
        var logFilePath = Path.Combine(_workDirectory, "adapter.log");
        var nlogConfigPath = Path.Combine(_workDirectory, "nlog.config");
        File.WriteAllText(nlogConfigPath,
            $$"""
             <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd">
               <targets>
                 <target name="file" type="File" fileName="{{logFilePath}}" layout="${level:uppercase=true}|${message}" />
               </targets>
               <rules>
                 <logger name="*" minlevel="Info" writeTo="file" />
               </rules>
             </nlog>
             """);
        var adapterBuilder = new AdapterBuilder();

        adapterBuilder.Run([],
            configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Adapter:NlogConfigPath"] = nlogConfigPath
            }),
            (_, services) =>
            {
                services.AddDataPipeline().RegisterEtlContext<IEtlContext>();
                services.AddSingleton(A.Fake<IAdapterService>());
                services.AddHostedService<FailingStartupService>();
            });

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, Environment.ExitCode);
        var errorLinesNamingTheReason = File.ReadAllLines(logFilePath)
            .Where(line => line.StartsWith("ERROR|", StringComparison.Ordinal))
            .Where(line => line.Contains(FailingStartupService.Reason, StringComparison.Ordinal))
            .ToList();
        var errorLine = Assert.Single(errorLinesNamingTheReason);
        Assert.Contains(typeof(InvalidOperationException).FullName!, errorLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostThatStopsOnRequestEndsTheProcessWithZero()
    {
        var adapterBuilder = new AdapterBuilder();

        adapterBuilder.Run([],
            (_, services) =>
            {
                services.AddDataPipeline().RegisterEtlContext<IEtlContext>();
                services.AddSingleton(A.Fake<IAdapterService>());
                services.AddHostedService<StopOnStartService>();
            });

        Assert.Equal(0, Environment.ExitCode);
    }

    private sealed class StopOnStartService(IHostApplicationLifetime applicationLifetime) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            applicationLifetime.StopApplication();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingStartupService : IHostedService
    {
        public const string Reason = "datastore host mongo-unresolvable does not resolve";

        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Reason);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
