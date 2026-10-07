using Meshmakers.Octo.Common.DistributionEventHub.Configuration;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
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

        // AB#4924 — a pool member is a different composition, not a flag on the dedicated one. It has
        // no AdapterRtId and no DedicatedTenantId, so the dedicated hub route, its execution service
        // and its health check cannot apply; and the dedicated IAdapterTenantScope must not be
        // registered at all, because a plain AddSingleton here would win over the lease-aware scope
        // that AddAdapterPoolMember() TryAdds from the caller's delegate. That override would fail
        // nowhere: the member would run, and simply never enforce a lease.
        // AB#5303 item 5 — see AdapterPoolMemberConfigurationGuard. A configured-but-unbound pool
        // section stops the host instead of silently producing a dedicated adapter.
        var poolMemberOptions = AdapterPoolMemberConfigurationGuard.BindAndVerify(builder.Configuration);
        var isPoolMember = poolMemberOptions.IsEnabled;

        builder.Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            // Central default: Information (override per deployment via OCTO_ADAPTER__MINIMUMLOGLEVEL).
            // RemoveLoggerFactoryFilter=false makes NLog honour this minimum instead of only the
            // per-repo nlog.config minlevel="Debug".
            loggingBuilder.SetMinimumLevel(startupOptions.MinimumLogLevel);
            LogManager.Setup().LoadConfigurationFromFile(startupOptions.NlogConfigPath);
            loggingBuilder.AddNLog(new NLogProviderOptions { RemoveLoggerFactoryFilter = false });
        });

        // 🔴 AB#5303 item 2 — this host NEVER honoured IgnoreCertificateValidation at all. Only
        // AdapterBuilder read it, and there it set ServicePointManager, which SocketsHttpHandler
        // ignores. The mesh adapter and every other WebAdapterBuilder host therefore had no switch,
        // while their hub connections bypassed validation unconditionally: the flag said one thing,
        // two different code paths did two others. One gate now, before anything opens a connection.
        if (startupOptions.IgnoreCertificateValidation)
        {
            ServerCertificateTrust.AllowAnyServerCertificate(
                new NLogLoggerProvider().CreateLogger(nameof(ServerCertificateTrust)));
        }

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
            // A pool member has no AdapterRtId; its member id is the stable identity it is known by.
            c.UniqueServiceAddress = isPoolMember
                ? $"adapterpool_{poolMemberOptions.EffectiveMemberId}"
                : $"adapter_{startupOptions.AdapterRtId}";

            configureDistributionEventHub?.Invoke(c);
        });

        builder.Services.AddOptions<AdapterHubClientOptions>()
            .Configure<IOptions<AdapterOptions>>((options, socketOptions) =>
            {
                // Connection-level: the adapter's OWN hub route (AB#4924).
                options.TenantId = socketOptions.Value.DedicatedTenantId;
                options.AdapterRtId = socketOptions.Value.AdapterRtId;
                options.AdapterCkTypeId = socketOptions.Value.AdapterCkTypeId;
                options.EndpointUri = socketOptions.Value.CommunicationControllerServicesUri;
            });

        if (!isPoolMember)
        {
            // AB#4924 increment 3 — see AdapterBuilder for the rationale. Skipped on a pool member:
            // AddAdapterPoolMember() supplies the lease-aware scope, and there is no process-wide
            // tenant to bind the legacy key to.
            builder.Services.AddSingleton<IAdapterTenantScope, AdapterTenantScope>();
            builder.Services
                .AddSingleton<IPostConfigureOptions<AdapterOptions>, ConfigureLegacyAdapterTenantId>();
        }

        builder.Services.AddSingleton<IPipelineRegistryService, PipelineRegistryService>();
        builder.Services.AddSingleton<IServiceClientAccessToken, ServiceClientAccessToken>();

        // AB#5072 - the adapter's own service credential for the controller's adapter hub.
        // The access-token holder above is a singleton because the SDK's SignalR client reads it on
        // every (re)connect: AdapterAccessTokenService writes into the very instance
        // AdapterHubClient was handed, so a refresh reaches the next reconnect without any
        // notification path. Registered as a hosted service BEFORE every other hosted service so its
        // StartAsync (which acquires the first token) completes before the hub connects - hosted
        // services are started sequentially.
        // AB#5303 item 4 — FIRST of all hosted services, before even the token service. A pool
        // member never touches MongoDB during startup (it has no tenant), so a wrong datastore host
        // stays invisible until the first lease; this makes it a startup failure that names the key.
        builder.Services.AddHostedService<DatastoreHostPreflight>();

        builder.Services
            .AddSingleton<IConfigureOptions<AuthenticatorOptions>, ConfigureAdapterAuthenticatorOptions>();
        builder.Services.AddSingleton<IAuthenticatorClient, AuthenticatorClient>();
        builder.Services.AddSingleton<AdapterAccessTokenService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<AdapterAccessTokenService>());

        builder.Services.AddSingleton<AdapterLifetimeManagement>();

        if (isPoolMember)
        {
            // The management connection, the registration on every (re)connect, and the heartbeat.
            // Without this the member connects to nothing and is never leased (AB#4924).
            builder.Services.AddHostedService<AdapterPoolMemberService>();
        }
        else
        {
            builder.Services.AddSingleton<AdapterHubCallbackService>();
            builder.Services.AddSingleton<IAdapterHubCallbacks>(provider =>
                provider.GetRequiredService<AdapterHubCallbackService>());
            builder.Services.AddSingleton<IAdapterHubCallbackService>(provider =>
                provider.GetRequiredService<AdapterHubCallbackService>());
            builder.Services.AddSingleton<IAdapterHubClient, AdapterHubClient>();
            builder.Services.AddSingleton<IPipelineExecutionReporter, AdapterPipelineExecutionReporter>();
            // AB#5231: this process has a controller connection, so a pipeline data event to a
            // pipeline on another workload can wake that workload before it is published.
            builder.Services.AddSingleton<IPipelineDataEventTargetWaker, HubPipelineDataEventTargetWaker>();
            builder.Services.AddTransient<IPipelineDebugger, AdapterPipelineDebugger>();
            // Shared registration state: written by AdapterExecutionService on every (re)registration,
            // read by the readiness check and the recovery watchdog (AB#5409). Dedicated adapters only:
            // a pool member has no hub registration state, so gating its readiness on it would keep
            // every member un-ready forever (member recovery is AP-I5).
            builder.Services.AddSingleton<IAdapterHubRegistrationState, AdapterHubRegistrationState>();
            builder.Services.AddSingleton<AdapterExecutionService>();
            builder.Services.AddHostedService<AdapterHealthFileService>();
            builder.Services.AddHostedService<AdapterMetricsSamplerService>();
            builder.Services.AddHostedService<AdapterHubRecoveryService>();

            // AdapterConnection reports the SignalR connection state for observability
            // (visible at /health) but is NOT tagged "ready" — it is satisfied by any connection
            // that is merely not Disconnected, which is not the same as being reachable.
            //
            // AdapterHubRegistration IS tagged "ready" (AB#5409): an adapter the communication
            // controller has no registration for cannot be configured, deployed to or called, so it
            // must not answer 200 on /healthz/ready. The old rationale for leaving readiness ungated
            // was that a probe must not depend on runtime data state (e.g. a tenant that is not
            // enabled yet) — that still holds, and is why the check reports healthy during
            // Adapter:HubReadinessGracePeriod and only ever turns unhealthy for an adapter that had
            // registered before or has long outlived its grace period.
            builder.Services.AddHealthChecks()
                .AddCheck<AdapterConnectionHealthCheck>(
                    "AdapterConnection",
                    HealthStatus.Unhealthy)
                .AddCheck<AdapterHubReadinessHealthCheck>(
                    "AdapterHubRegistration",
                    HealthStatus.Unhealthy,
                    tags: ["ready"]);

            if (startupOptions.UseHostedService)
            {
                builder.Services.AddHostedService<HostedAdapterExecutionService>();
            }
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