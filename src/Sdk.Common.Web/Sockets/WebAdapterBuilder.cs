using Meshmakers.Octo.Common.DistributionEventHub.Configuration;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Meshmakers.Octo.Sdk.Common.Web.Sockets;

/// <summary>
///     The adapter builder is used to start up an adapter using asp.net.
/// </summary>
public class WebAdapterBuilder
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Executes the startup of a socket.
    /// </summary>
    /// <param name="args">Program arguments</param>
    /// <param name="configureServicesDelegate">A delegate to configure additional services</param>
    /// <param name="configureApp">A delegate to configure apps</param>
    /// <param name="configureDistributionEventHub">Configuration of the distribution event hub</param>
    public async Task RunAsync(string[] args, Action<IHostApplicationBuilder> configureServicesDelegate,
        Action<WebApplication> configureApp,
        Action<IDistributionEventHubConfiguration>? configureDistributionEventHub = null)
    {
        try
        {
            Logger.Info("Octo Adapter, Version {ProductVersion}",
                AssemblyMetadataReader.GetProductVersion());
            Logger.Info("{Copyright}", AssemblyMetadataReader.GetCopyright());

            var builder = CreateHostBuilder(args, configureServicesDelegate, configureDistributionEventHub);
            var app = builder.Build();

            var schemaOutputPath = GetSchemaOutputPath(args);
            if (schemaOutputPath != null)
            {
                GeneratePipelineSchema(app.Services, schemaOutputPath);
                return;
            }

            configureApp(app);

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Stopped socket because of exception");
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }

    private static WebApplicationBuilder CreateHostBuilder(string[] args,
        Action<IHostApplicationBuilder> configureServicesDelegate,
        Action<IDistributionEventHubConfiguration>? configureDistributionEventHub)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddEnvironmentVariables("OCTO_").AddCommandLine(args);
        configureServicesDelegate(builder);

        builder.Services.Configure<AdapterOptions>(options =>
            builder.Configuration.GetSection("Adapter").Bind(options));

        var startupOptions = new AdapterOptions();
        builder.Configuration.GetSection("Adapter").Bind(startupOptions);

        builder.Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
            loggingBuilder.AddNLog("nlog.config");
        });

        builder.Services.AddDistributionEventHubWithOptions(s =>
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

        builder.Services.AddOptions<AdapterHubClientOptions>()
            .Configure<IOptions<AdapterOptions>>((options, socketOptions) =>
            {
                options.TenantId = socketOptions.Value.TenantId;
                options.AdapterRtId = socketOptions.Value.AdapterRtId;
                options.AdapterCkTypeId = socketOptions.Value.AdapterCkTypeId;
                options.EndpointUri = socketOptions.Value.CommunicationControllerServicesUri;
            });

        builder.Services.AddSingleton<IPipelineRegistryService, PipelineRegistryService>();
        builder.Services.AddSingleton<IServiceClientAccessToken, ServiceClientAccessToken>();

        builder.Services.AddSingleton<AdapterLifetimeManagement>();

        builder.Services.AddSingleton<AdapterHubCallbackService>();
        builder.Services.AddSingleton<IAdapterHubCallbacks>(provider =>
            provider.GetRequiredService<AdapterHubCallbackService>());
        builder.Services.AddSingleton<IAdapterHubCallbackService>(provider =>
            provider.GetRequiredService<AdapterHubCallbackService>());
        builder.Services.AddSingleton<IAdapterHubClient, AdapterHubClient>();
        builder.Services.AddSingleton<IPipelineExecutionReporter, AdapterPipelineExecutionReporter>();
        builder.Services.AddTransient<IPipelineDebugger, AdapterPipelineDebugger>();
        builder.Services.AddSingleton<AdapterExecutionService>();
        builder.Services.AddHostedService<AdapterHealthFileService>();
        builder.Services.AddHostedService<AdapterMetricsSamplerService>();

        // AdapterConnection reports the SignalR connection state for observability
        // (visible at /health) but is NOT tagged "ready" — kubelet's readiness probe
        // must not depend on runtime data state (e.g. tenant enabled in OctoMesh),
        // otherwise a not-yet-enabled tenant blocks deployment indefinitely.
        builder.Services.AddHealthChecks()
            .AddCheck<AdapterConnectionHealthCheck>(
                "AdapterConnection",
                HealthStatus.Unhealthy);

        if (startupOptions.UseHostedService)
        {
            builder.Services.AddHostedService<HostedAdapterExecutionService>();
        }


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

            Logger.Error("Missing output path after {Flag}", flag);
            return null;
        }

        return null;
    }

    private static void GeneratePipelineSchema(IServiceProvider services, string outputPath)
    {
        var schemaGenerator = services.GetService<IPipelineSchemaGenerator>();
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
        Logger.Info("Pipeline schema written to {OutputPath}", outputPath);
    }
}