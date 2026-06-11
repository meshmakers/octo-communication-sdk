using System.Net;
using Meshmakers.Octo.Common.DistributionEventHub.Configuration;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     The adapter builder is used to start up an adapter.
/// </summary>
public class AdapterBuilder
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Executes the startup of an adapter.
    /// </summary>
    /// <param name="args">Program arguments</param>
    /// <param name="postConfigureDelegate">A delegate to configure additional services after the SDK itself has been initialized.</param>
    /// <param name="configureDistributionEventHub">Configuration of the distribution event hub</param>
    public void Run(string[] args, Action<HostBuilderContext, IServiceCollection> postConfigureDelegate,
        Action<IDistributionEventHubConfiguration>? configureDistributionEventHub = null)
    {
        Run(args, null, postConfigureDelegate, configureDistributionEventHub);
    }

    /// <summary>
    ///     Executes the startup of an adapter.
    /// </summary>
    /// <param name="args">Program arguments</param>
    /// <param name="configureConfiguration">A delegate to build configuration.</param>
    /// <param name="configureServices">A delegate to configure additional services after the SDK itself has been initialized.</param>
    /// <param name="configureDistributionEventHub">Configuration of the distribution event hub</param>
    // ReSharper disable once MemberCanBePrivate.Global
    public void Run(string[] args, Action<IConfigurationBuilder>? configureConfiguration,
        Action<HostBuilderContext, IServiceCollection> configureServices,
        Action<IDistributionEventHubConfiguration>? configureDistributionEventHub = null)
    {
        try
        {
            Logger.Info($"Octo Mesh Adapter, Version {AssemblyMetadataReader.GetProductVersion()}");
            Logger.Info(AssemblyMetadataReader.GetCopyright());

            var host = CreateHostBuilder(args, configureConfiguration, configureServices,
                configureDistributionEventHub).Build();

            var schemaOutputPath = GetSchemaOutputPath(args);
            if (schemaOutputPath != null)
            {
                GeneratePipelineSchema(host, schemaOutputPath);
                return;
            }

            host.Run();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Stopped adapter because of exception");
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }

    /// <summary>
    /// Stops the adapter gracefully.
    /// </summary>
    public void Stop()
    {
        AdapterLifetimeManagement.Instance?.Stop();
    }

    private static IHostBuilder CreateHostBuilder(string[] args,
        Action<IConfigurationBuilder>? configureConfiguration,
        Action<HostBuilderContext, IServiceCollection> configureServices,
        Action<IDistributionEventHubConfiguration>? configureDistributionEventHub)
    {
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureHostConfiguration(config =>
        {
            config
                .AddEnvironmentVariables("OCTO_")
                .AddCommandLine(args);

            configureConfiguration?.Invoke(config);
        });


        builder.ConfigureServices((b, services) =>
        {
            configureServices(b, services);

            services.Configure<AdapterOptions>(options =>
                b.Configuration.GetSection("Adapter").Bind(options));

            var startupOptions = new AdapterOptions();
            b.Configuration.GetSection("Adapter").Bind(startupOptions);

            services.Configure<EdgeDataBufferConfiguration>(options =>
                b.Configuration.GetSection("EdgeDataBuffer").Bind(options));

            // Continue with configuration
            services.AddSingleton<AdapterLifetimeManagement>();

            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                loggingBuilder.AddNLog(startupOptions.NlogConfigPath);
            });

            services.AddDistributionEventHubWithOptions(s =>
            {
                s.InstancePrefix = startupOptions.InstancePrefix;
                s.BrokerHost = startupOptions.BrokerHost;
                s.BrokerPort = startupOptions.BrokerPort;
                s.BrokerUser = startupOptions.BrokerUsername;
                s.BrokerPassword = startupOptions.BrokerPassword;
            }, c =>
            {
                c.AutomaticallyStartBusDuringStartup = false;
                c.UniqueServiceAddress = $"adapter_{startupOptions.AdapterRtId}";
                configureDistributionEventHub?.Invoke(c);
            });

            if (startupOptions.IgnoreCertificateValidation)
            {
#pragma warning disable SYSLIB0014
                // needs to be handled by AB#1677
                ServicePointManager.ServerCertificateValidationCallback += (_, _, _, _) => true;
#pragma warning restore SYSLIB0014
            }

            services.AddOptions<AdapterHubClientOptions>()
                .Configure<IOptions<AdapterOptions>>((options, toolOptions) =>
                {
                    options.TenantId = toolOptions.Value.TenantId;
                    options.AdapterRtId = toolOptions.Value.AdapterRtId;
                    options.AdapterCkTypeId = toolOptions.Value.AdapterCkTypeId;
                    options.EndpointUri = toolOptions.Value.CommunicationControllerServicesUri;
                });

            services.AddSingleton<IPipelineRegistryService, PipelineRegistryService>();
            services.AddSingleton<IServiceClientAccessToken, ServiceClientAccessToken>();

            services.AddSingleton<AdapterHubCallbackService>();
            services.AddSingleton<IAdapterHubCallbacks>(provider =>
                provider.GetRequiredService<AdapterHubCallbackService>());
            services.AddSingleton<IAdapterHubCallbackService>(provider =>
                provider.GetRequiredService<AdapterHubCallbackService>());
            services.AddSingleton<IAdapterHubClient, AdapterHubClient>();
            services.AddSingleton<IPipelineExecutionReporter, AdapterPipelineExecutionReporter>();
            services.AddTransient<IPipelineDebugger, AdapterPipelineDebugger>();

            services.AddSingleton<AdapterExecutionService>();
            services.AddHostedService<AdapterHealthFileService>();
            services.AddHostedService<AdapterMetricsSamplerService>();

            if (startupOptions.UseHostedService)
            {
                services.AddHostedService<HostedAdapterExecutionService>();
            }
        });

        return builder;
    }

    private static string? GetSchemaOutputPath(string[] args)
    {
        const string flag = "--generate-pipeline-schema";
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length)
            {
                return args[i + 1];
            }

            Logger.Error($"Missing output path after {flag}");
            return null;
        }

        return null;
    }

    private static void GeneratePipelineSchema(IHost host, string outputPath)
    {
        var schemaGenerator = host.Services.GetService<IPipelineSchemaGenerator>();
        if (schemaGenerator == null)
        {
            Logger.Error("IPipelineSchemaGenerator is not registered. Ensure AddDataPipeline() has been called.");
            return;
        }

        var schema = schemaGenerator.GenerateSchema();

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, schema);
        Logger.Info($"Pipeline schema written to {outputPath}");
    }
}