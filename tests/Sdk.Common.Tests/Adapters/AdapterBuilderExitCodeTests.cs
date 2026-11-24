using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     The builder shuts down the global NLog configuration, so these tests run alone.
/// </summary>
[CollectionDefinition(nameof(AdapterBuilderProcessCollection), DisableParallelization = true)]
public sealed class AdapterBuilderProcessCollection;

/// <summary>
///     The builder ends the process with a non-zero exit code when its host cannot be built or started,
///     so Kubernetes reports a crash instead of a clean exit.
/// </summary>
[Collection(nameof(AdapterBuilderProcessCollection))]
public sealed class AdapterBuilderExitCodeTests : IDisposable
{
    private const string StartupFailureReason = "datastore host mongo-unresolvable does not resolve";

    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "adapter-builder-exit-code-" + Guid.NewGuid().ToString("N"));

    private int? _terminatedWithExitCode;

    public AdapterBuilderExitCodeTests()
    {
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_workDirectory, recursive: true);
    }

    private AdapterBuilder CreateAdapterBuilder() =>
        new() { TerminateProcess = exitCode => _terminatedWithExitCode = exitCode };

    private static void RegisterMinimalAdapter(IServiceCollection services)
    {
        services.AddDataPipeline().RegisterEtlContext<IEtlContext>();
        services.AddSingleton(A.Fake<IAdapterService>());
    }

    [Fact]
    public void AConfigurationThatDoesNotBindEndsTheProcessWithANonZeroExitCodeAndNamesTheReason()
    {
        var memoryTarget = new MemoryTarget("memory") { Layout = "${level:uppercase=true}|${message}" };
        var loggingConfiguration = new LoggingConfiguration();
        loggingConfiguration.AddRuleForAllLevels(memoryTarget);
        LogManager.Configuration = loggingConfiguration;

        CreateAdapterBuilder().Run([],
            configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AdapterPoolMemberOptions.SectionName}:AdapterPoolTenantId"] = "accounting"
            }),
            (_, _) => { });

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, _terminatedWithExitCode);
        var errorLines = memoryTarget.Logs
            .Where(line => line.StartsWith("ERROR|", StringComparison.Ordinal))
            .ToList();
        var errorLine = Assert.Single(errorLines);
        Assert.Contains(typeof(AdapterException).FullName!, errorLine, StringComparison.Ordinal);
        Assert.Contains("AdapterPoolRtId", errorLine, StringComparison.Ordinal);
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

        CreateAdapterBuilder().Run([],
            configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Adapter:NlogConfigPath"] = nlogConfigPath
            }),
            (_, services) =>
            {
                RegisterMinimalAdapter(services);
                services.AddSingleton<IHostedService>(_ =>
                    throw new InvalidOperationException(StartupFailureReason));
            });

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, _terminatedWithExitCode);
        var errorLinesNamingTheReason = File.ReadAllLines(logFilePath)
            .Where(line => line.StartsWith("ERROR|", StringComparison.Ordinal))
            .Where(line => line.Contains(StartupFailureReason, StringComparison.Ordinal))
            .ToList();
        var errorLine = Assert.Single(errorLinesNamingTheReason);
        Assert.Contains(typeof(InvalidOperationException).FullName!, errorLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostThatStopsOnRequestDoesNotTerminateTheProcess()
    {
        CreateAdapterBuilder().Run([],
            (_, services) =>
            {
                RegisterMinimalAdapter(services);
                services.AddHostedService<StopOnStartService>();
            });

        Assert.Null(_terminatedWithExitCode);
    }

    [Fact]
    public void ABackgroundServiceThatFailsAfterTheStartEndsTheProcessWithANonZeroExitCode()
    {
        var memoryTarget = new MemoryTarget("memory") { Layout = "${level:uppercase=true}|${message}" };
        var loggingConfiguration = new LoggingConfiguration();
        loggingConfiguration.AddRuleForAllLevels(memoryTarget);
        LogManager.Configuration = loggingConfiguration;

        CreateAdapterBuilder().Run([],
            (_, services) =>
            {
                RegisterMinimalAdapter(services);
                services.AddHostedService<FailingBackgroundService>();
            });

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, _terminatedWithExitCode);
        Assert.Contains(memoryTarget.Logs, line =>
            line.StartsWith("ERROR|Adapter stopped because of an exception", StringComparison.Ordinal) &&
            line.Contains(FailingBackgroundService.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void ABackgroundServiceThatCancelsItselfBeforeAShutdownEndsTheProcessWithANonZeroExitCode()
    {
        CreateAdapterBuilder().Run([],
            (_, services) =>
            {
                RegisterMinimalAdapter(services);
                services.AddHostedService<SelfCancellingBackgroundService>();
            });

        Assert.Equal(AdapterHostRunGuard.FailureExitCode, _terminatedWithExitCode);
    }

    private sealed class SelfCancellingBackgroundService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
            throw new TaskCanceledException("upstream request timed out");
        }
    }

    private sealed class FailingBackgroundService : BackgroundService
    {
        public const string Reason = "lease loop failed";

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
            throw new InvalidOperationException(Reason);
        }
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
}
