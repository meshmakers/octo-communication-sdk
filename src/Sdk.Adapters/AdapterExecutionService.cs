using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Options;
using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Background service for the execution of an adapter.
/// </summary>
public class AdapterExecutionService : IAdapterHubCallbacks
{
    private readonly IAdapterHubClient _hubClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly IOptions<AdapterOptions> _adapterOptions;
    private readonly IAdapterService _adapterService;
    private readonly IPipelineExecutionReporter? _executionReporter;
    private readonly INodeSchemaRegistry? _nodeSchemaRegistry;
    private readonly IPipelineSchemaGenerator? _pipelineSchemaGenerator;
    private readonly IPipelineRegistryService _pipelineRegistryService;
    private readonly IAdapterHubRegistrationState _registrationState;
    private readonly SemaphoreSlim _configurationUpdateLock = new(1, 1);

    // Captured once per process. Used on fresh startup to tell the controller which of this
    // adapter's executions predate the current process and are therefore orphans (AB#4280).
    private readonly DateTime _processStartUtc = DateTime.UtcNow;

    /// <summary>
    /// Creates a new instance of <see cref="AdapterExecutionService"/>.
    /// </summary>
    /// <param name="adapterHubClient"></param>
    /// <param name="adapterOptions"></param>
    /// <param name="adapterService"></param>
    /// <param name="adapterHubCallbackService"></param>
    /// <param name="adapterLifetimeManagement"></param>
    /// <param name="pipelineRegistryService"></param>
    /// <param name="executionReporter"></param>
    /// <param name="nodeSchemaRegistry"></param>
    /// <param name="pipelineSchemaGenerator"></param>
    /// <param name="registrationState">
    /// Tracks whether the adapter is registered at the adapter hub (AB#5409). Optional so existing
    /// callers keep compiling; the DI container supplies the shared singleton the readiness check and
    /// the recovery watchdog read.
    /// </param>
    public AdapterExecutionService(IAdapterHubClient adapterHubClient,
        IOptions<AdapterOptions> adapterOptions, IAdapterService adapterService,
        IAdapterHubCallbackService adapterHubCallbackService,
        [SuppressMessage("ReSharper", "UnusedParameter.Local")]
        AdapterLifetimeManagement adapterLifetimeManagement,
        IPipelineRegistryService pipelineRegistryService,
        IPipelineExecutionReporter? executionReporter = null,
        INodeSchemaRegistry? nodeSchemaRegistry = null,
        IPipelineSchemaGenerator? pipelineSchemaGenerator = null,
        IAdapterHubRegistrationState? registrationState = null)
    {
        _adapterService = adapterService;
        _hubClient = adapterHubClient;
        _adapterOptions = adapterOptions;
        _pipelineRegistryService = pipelineRegistryService;
        _executionReporter = executionReporter;
        _nodeSchemaRegistry = nodeSchemaRegistry;
        _pipelineSchemaGenerator = pipelineSchemaGenerator;
        _registrationState = registrationState ?? new AdapterHubRegistrationState();

        // AdapterLifetimeManagement is used to stop the adapter from an external source
        // it only needs to be created via DI container and is then accessed from the outside
        // which only happens if a service requires it in the constructor. So that's why the unused
        // parameter is not removed.

        adapterHubCallbackService.RegisterCallback(this);
    }

    private const int TenantUpdateMaxRetries = 3;
    private static readonly TimeSpan TenantUpdateBaseDelay = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public Task PreUpdateTenantAsync(string tenantId)
    {
        _logger.Info("PreUpdateTenantAsync for tenant {TenantId}", tenantId);

        // Run on a background thread to avoid deadlocks on the SignalR callback thread.
        _ = Task.Run(async () =>
        {
            try
            {
                var cancellationToken = CancellationToken.None;
                await StopAsync(cancellationToken);

                for (var attempt = 1; attempt <= TenantUpdateMaxRetries; attempt++)
                {
                    var delay = TimeSpan.FromTicks(TenantUpdateBaseDelay.Ticks * attempt);
                    _logger.Info(
                        "Waiting {DelaySeconds}s before reconnecting to service (attempt {Attempt}/{MaxRetries})...",
                        delay.TotalSeconds, attempt, TenantUpdateMaxRetries);
                    await Task.Delay(delay, cancellationToken);

                    try
                    {
                        await StartAsync(cancellationToken);
                        _logger.Info("PreUpdateTenantAsync for tenant {TenantId} completed successfully on attempt {Attempt}",
                            tenantId, attempt);
                        return;
                    }
                    catch (Exception e) when (attempt < TenantUpdateMaxRetries)
                    {
                        _logger.Warn(e,
                            "StartAsync failed for tenant {TenantId} on attempt {Attempt}/{MaxRetries}, will retry",
                            tenantId, attempt, TenantUpdateMaxRetries);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error during PreUpdateTenantAsync for tenant {TenantId}", tenantId);
            }
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CkModelChangedAsync(string tenantId)
    {
        // The controller broadcasts this to every connected adapter (the adapter cache on the
        // controller may be stale or wiped during a tenant update); only react for our own tenant.
        // Process-level filter on a broadcast: "is this OUR tenant" is a question about the
        // adapter, not about any execution (AB#4924).
        if (!string.Equals(tenantId, _adapterOptions.Value.DedicatedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.Info("CkModelChangedAsync for tenant {TenantId}, invalidating CK model cache", tenantId);
        try
        {
            await _adapterService.CkModelChangedAsync(tenantId);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error during CkModelChangedAsync for tenant {TenantId}", tenantId);
        }
    }

    /// <inheritdoc />
    public Task AdapterConfigurationUpdatedAsync(string tenantId, AdapterConfigurationDto adapterConfiguration)
    {
        _logger.Info("AdapterConfigurationUpdatedAsync for tenant {TenantId}", tenantId);

        // Run shutdown/startup on a background thread to avoid deadlocks.
        // This method is called on the SignalR callback thread. The MassTransit bus stop
        // during shutdown may wait for in-flight consumers that need the SignalR thread,
        // causing a deadlock if we await directly here.
        _ = Task.Run(() => ApplyConfigurationUpdateAsync(tenantId, adapterConfiguration));

        return Task.CompletedTask;
    }

    internal TimeSpan ConfigurationUpdateLockTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Bounds every wait on the adapter implementation's <see cref="IAdapterService.ShutdownAsync"/>.
    /// An unbounded shutdown hang (stuck trigger stop / bus stop) froze the nightly
    /// PreUpdateTenant restart for days and left the adapter without a hub connection (AB#4876).
    /// </summary>
    internal TimeSpan AdapterShutdownTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Bounds hub invokes on the stop path — an unregister over a half-dead connection must not
    /// block the restart sequence (AB#4876).
    /// </summary>
    internal TimeSpan HubInvokeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bounds the register invoke of the (re)connect path. Observed live (AB#4968): the invoke
    /// blocked silently for six minutes — the SignalR watchdog does not cover it because the
    /// connection state stays Connected — and nothing in the log pointed at the stall. On
    /// timeout the start loop retries visibly; registration is idempotent on the controller.
    /// </summary>
    internal TimeSpan RegisterInvokeTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How often a single (re)connect attempt tries to register at the adapter hub before it hands
    /// the failure back to the SignalR (re)connect loop (AB#5409). Bounded on purpose: only the loop
    /// can force-stop and re-establish a dead connection, so retrying here forever would keep the
    /// adapter on a connection that cannot be repaired from this layer.
    /// </summary>
    internal int RegistrationMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Base back-off between registration attempts; multiplied by the attempt number.
    /// </summary>
    internal TimeSpan RegistrationRetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a registration attempt waits for the hub client to report a connection before it
    /// invokes anyway. Invoking on a Disconnected connection can only fail, and failing fast here
    /// would burn the bounded attempts for nothing.
    /// </summary>
    internal TimeSpan HubConnectionWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Poll interval of <see cref="HubConnectionWaitTimeout"/>.
    /// </summary>
    internal TimeSpan HubConnectionPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Waits until the hub client reports a connection, bounded by
    /// <see cref="HubConnectionWaitTimeout"/>. Never throws on timeout — the caller invokes anyway
    /// and lets the retry (and ultimately the reconnect loop) deal with the failure.
    /// </summary>
    private async Task WaitForHubConnectionAsync(CancellationToken cancellationToken)
    {
        if (_hubClient.IsAlive)
        {
            return;
        }

        _logger.Info("Waiting up to {Timeout} for the adapter hub connection to become active before registering",
            HubConnectionWaitTimeout);

        var waited = TimeSpan.Zero;
        while (waited < HubConnectionWaitTimeout && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(HubConnectionPollInterval, cancellationToken);
            waited += HubConnectionPollInterval;

            if (_hubClient.IsAlive)
            {
                return;
            }
        }

        _logger.Warn("Adapter hub connection did not become active within {Timeout}, registering anyway",
            HubConnectionWaitTimeout);
    }

    private readonly object _pendingConfigurationGate = new();
    private (string TenantId, AdapterConfigurationDto Configuration)? _pendingConfigurationUpdate;

    private async Task ApplyConfigurationUpdateAsync(string tenantId, AdapterConfigurationDto adapterConfiguration)
    {
        // Last-writer-wins: the newest update is parked in a one-element slot and drained by
        // whoever holds the lock, so a lock timeout can no longer discard an update and leave
        // the adapter permanently on a stale configuration (AB#4559).
        lock (_pendingConfigurationGate)
        {
            _pendingConfigurationUpdate = (tenantId, adapterConfiguration);
        }

        _logger.Info("Waiting to acquire configuration update lock for tenant {TenantId}", tenantId);
        var lockStopwatch = Stopwatch.StartNew();
        if (!await _configurationUpdateLock.WaitAsync(ConfigurationUpdateLockTimeout, CancellationToken.None))
        {
            _logger.Warn(
                "Timed out after {ElapsedMs}ms waiting for configuration update lock for tenant {TenantId}. " +
                "The update stays queued and is applied by the in-progress update when it completes.",
                lockStopwatch.ElapsedMilliseconds, tenantId);
            return;
        }

        _logger.Info("Acquired configuration update lock for tenant {TenantId} after {ElapsedMs}ms",
            tenantId, lockStopwatch.ElapsedMilliseconds);

        try
        {
            await DrainPendingConfigurationUpdatesAsync();
        }
        finally
        {
            _configurationUpdateLock.Release();
        }
    }

    /// <summary>
    /// Applies every configuration update parked in the pending slot. Must be called while
    /// holding <see cref="_configurationUpdateLock"/>. Shared between the push path
    /// (<see cref="ApplyConfigurationUpdateAsync"/>) and the registration path, which drains
    /// updates that arrived (or timed out waiting) while the initial startup held the lock
    /// (AB#4806).
    /// </summary>
    private async Task DrainPendingConfigurationUpdatesAsync()
    {
        while (true)
        {
            (string TenantId, AdapterConfigurationDto Configuration)? pending;
            lock (_pendingConfigurationGate)
            {
                pending = _pendingConfigurationUpdate;
                _pendingConfigurationUpdate = null;
            }

            if (pending == null)
            {
                break;
            }

            await ApplyConfigurationUpdateCoreAsync(pending.Value.TenantId, pending.Value.Configuration);
        }
    }

    private async Task ApplyConfigurationUpdateCoreAsync(string tenantId,
        AdapterConfigurationDto adapterConfiguration)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var cancellationToken = CancellationToken.None;

        try
        {
            List<DeploymentUpdateErrorMessageDto> deploymentErrorMessages = [];

            // Apply the adapter-level configuration BEFORE the pipeline update so that any pipeline
            // (re)registered by the selective update below immediately observes the new configuration.
            // Pipelines are updated separately by UpdatePipelinesAsync; the adapter must not touch them here.
            _logger.Info("Applying adapter configuration update for tenant {TenantId}", tenantId);
            await _adapterService.ConfigurationUpdatedAsync(tenantId, adapterConfiguration, cancellationToken);

            _logger.Info("Starting selective pipeline update for tenant {TenantId}", tenantId);
            var stepStopwatch = Stopwatch.StartNew();
            var startupSuccess = await _pipelineRegistryService.UpdatePipelinesAsync(
                tenantId, adapterConfiguration.Pipelines, deploymentErrorMessages);
            _logger.Info(
                "Selective pipeline update completed for tenant {TenantId} in {ElapsedMs}ms, success={Success}",
                tenantId, stepStopwatch.ElapsedMilliseconds, startupSuccess);

            var rtEntityId = GetAdapterRtEntityId();
            await _hubClient.SendDeploymentUpdateResultAsync(rtEntityId,
                new DeploymentResult { IsSuccess = startupSuccess, ErrorMessages = deploymentErrorMessages });

            _logger.Info(
                "Configuration update completed for tenant {TenantId} in {TotalElapsedMs}ms",
                tenantId, totalStopwatch.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            _logger.Error(e,
                "Error during configuration update for tenant {TenantId} after {ElapsedMs}ms",
                tenantId, totalStopwatch.ElapsedMilliseconds);
            try
            {
                var rtEntityId = GetAdapterRtEntityId();
                await _hubClient.SendDeploymentUpdateResultAsync(rtEntityId,
                    new DeploymentResult
                    {
                        IsSuccess = false,
                        ErrorMessages =
                        [
                            new DeploymentUpdateErrorMessageDto
                                { ErrorCategory = DeploymentErrorCategories.Uncategorized, ErrorMessage = e.Message }
                        ]
                    });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send deployment error result for tenant {TenantId}", tenantId);
            }
        }
    }

    /// <summary>
    /// Starts the adapter execution service.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            async Task ReConnectFunction(bool isReconnect)
            {
                try
                {
                    var rtEntityId = GetAdapterRtEntityId();

                    async Task<AdapterConfigurationDto> InvokeRegisterAsync()
                    {
                        var nodeDescriptorDtos = GetNodeDescriptorDtos();
                        var pipelineSchemaJson = GetPipelineSchemaJson();
                        Task<AdapterConfigurationDto> registerTask;
                        if (nodeDescriptorDtos != null && pipelineSchemaJson != null)
                        {
                            _logger.Info("Registering with {NodeCount} node descriptors and pipeline schema", nodeDescriptorDtos.Count);
                            registerTask = _hubClient.RegisterAdapterWithSchemaAsync(rtEntityId, nodeDescriptorDtos, pipelineSchemaJson);
                        }
                        else if (nodeDescriptorDtos != null)
                        {
                            _logger.Info("Registering with {NodeCount} node descriptors", nodeDescriptorDtos.Count);
                            registerTask = _hubClient.RegisterAdapterWithNodesAsync(rtEntityId, nodeDescriptorDtos);
                        }
                        else
                        {
                            registerTask = _hubClient.RegisterAdapterAsync(rtEntityId);
                        }

                        return await registerTask.WaitAsync(RegisterInvokeTimeout, cancellationToken);
                    }

                    // Registration is retried in place, bounded, before the failure is handed back to
                    // the SignalR (re)connect loop (AB#5409). The observed failure is
                    // `InvokeCoreAsync cannot be called if the connection is not active`: the register
                    // invoke races the connection state right after StartAsync returned. On a fresh
                    // start the loop's next iteration papers over it; after a long controller outage
                    // one such throw was all it took to leave the adapter connected-but-unregistered —
                    // the controller then answers every deploy with "has no live SignalR connection",
                    // while the pod stays 1/1 Running.
                    //
                    // The pre-check uses IsAlive, which is `State != Disconnected` and therefore also
                    // true while the connection is still Connecting — exactly the window that produces
                    // the exception. A strict `IsConnected` would have to be added to
                    // ISignalRClient in octo-sdk and shipped as a NuGet, so the actual wait for an
                    // active connection is the retry: the next attempt runs after a back-off, by which
                    // time the connection has either finished connecting or is gone for good.
                    async Task<AdapterConfigurationDto> RegisterAtHubAsync()
                    {
                        for (var attempt = 1;; attempt++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await WaitForHubConnectionAsync(cancellationToken);

                            _logger.Info("Registering at adapter hub (attempt {Attempt}/{MaxAttempts})",
                                attempt, RegistrationMaxAttempts);
                            try
                            {
                                var registeredConfiguration = await InvokeRegisterAsync();
                                _registrationState.MarkRegistered();
                                _logger.Info("Registration successfull");
                                return registeredConfiguration;
                            }
                            catch (Exception e) when (e is not OperationCanceledException
                                                      && e is not ObjectDisposedException
                                                      && attempt < RegistrationMaxAttempts)
                            {
                                _registrationState.MarkNotRegistered(e.Message);

                                // Error, not Warn: the adapter log carries WARN-level audit noise by
                                // the dozen per pipeline run (AB#5409 item 3) and rotates the startup
                                // log away within the hour. A retried registration has to stay
                                // findable afterwards.
                                var delay = TimeSpan.FromTicks(RegistrationRetryBaseDelay.Ticks * attempt);
                                _logger.Error(e,
                                    "Registration at adapter hub failed on attempt {Attempt}/{MaxAttempts}, retrying in {DelaySeconds}s",
                                    attempt, RegistrationMaxAttempts, delay.TotalSeconds);
                                await Task.Delay(delay, cancellationToken);
                            }
                            catch (Exception e)
                            {
                                _registrationState.MarkNotRegistered(e.Message);
                                throw;
                            }
                        }
                    }

                    List<DeploymentUpdateErrorMessageDto> deploymentErrorMessages = [];
                    if (!isReconnect)
                    {
                        var tenantId = _adapterOptions.Value.DedicatedTenantId;
                        if (string.IsNullOrWhiteSpace(tenantId) || tenantId == null)
                        {
                            return;
                        }

                        // The register-return configuration applied here and the controller's
                        // reconcile push (AdapterConfigurationUpdatedAsync, fired on every
                        // registration since AB#4594) race when applied concurrently — pipelines
                        // ended up registered twice, leaving orphaned bus consumers that block the
                        // exclusive command queues with RESOURCE_LOCKED (AB#4806).
                        // The lock is taken BEFORE the register invoke, and that ordering is
                        // load-bearing (AB#4968): the controller fires the push from inside the
                        // register RPC, so taking the lock only afterwards let the push win the
                        // race — it then registered every pipeline (bus triggers included) on an
                        // adapter whose event hub had never started, the ensure-shutdown below
                        // hung stopping those triggers, and its abandoned continuation kept the
                        // pipeline registry lock forever: Configured with zero routes, no
                        // recovery. With the lock held from here on, a push arriving during
                        // registration parks in the pending slot and is drained below, where the
                        // value-equality check turns an identical reconcile push into a no-op.
                        // Bounded wait: a lock holder stuck in an unbounded await would otherwise
                        // freeze the SignalR start loop forever with no log line (AB#4876). The
                        // throw is caught by the start loop, which retries visibly.
                        _logger.Info("Waiting for configuration update lock before initial adapter startup");
                        if (!await _configurationUpdateLock.WaitAsync(ConfigurationUpdateLockTimeout, cancellationToken))
                        {
                            throw new TimeoutException(
                                $"Timed out after {ConfigurationUpdateLockTimeout} waiting for the configuration update lock before initial adapter startup");
                        }
                        bool success;
                        try
                        {
                            var configuration = await RegisterAtHubAsync();

                            // Ensure that the adapter is shutdown before starting it up again
                            // to stop e.g. trigger nodes that are still running
                            _logger.Info("Ensure shutdown adapter before startup.");
                            await ShutdownAdapterBoundedAsync(tenantId, cancellationToken);
                            _logger.Info("Shutdown adapter before startup done.");

                            _logger.Info("Startup of adapter is executed.");
                            success = await _adapterService.StartupAsync(
                                new AdapterStartup { TenantId = tenantId, Configuration = configuration },
                                deploymentErrorMessages, cancellationToken);
                            _logger.Info("Startup of adapter done.");

                            // Fresh process: resolve executions orphaned by the previous process.
                            // Its in-memory tasks were lost on restart, so the controller fails any of
                            // this adapter's Running/Interrupted executions that started before this
                            // process began — they can no longer complete or report a result (AB#4280).
                            if (_executionReporter != null)
                            {
                                _logger.Info("Resolving executions orphaned by a previous adapter process (if any).");
                                await _executionReporter.FailOrphanedExecutionsAsync(_processStartUtc);
                            }

                            _logger.Info("Sending deployment result to adapter hub");
                            await _hubClient.SendDeploymentUpdateResultAsync(rtEntityId,
                                new DeploymentResult { IsSuccess = success, ErrorMessages = deploymentErrorMessages });
                            _logger.Info("Deployment result sent to adapter hub");

                            // Apply configuration pushes that were parked while startup held the lock,
                            // AFTER the registration result went out so the controller sees the states
                            // in the order they were produced.
                            await DrainPendingConfigurationUpdatesAsync();
                        }
                        finally
                        {
                            _configurationUpdateLock.Release();
                        }
                    }
                    else
                    {
                        await RegisterAtHubAsync();

                        // AB#5415: CkModelChanged is a fire-and-forget broadcast to the connections
                        // the controller currently holds, so every CK model import that completed
                        // while this adapter was away is lost for good. Nothing else ever re-reads a
                        // loaded CK cache, so without this flush the adapter keeps validating against
                        // the model it held when the connection dropped - the observed failure was a
                        // CkCacheException on every execution for days after the imported model had
                        // already repaired the tenant. Unconditional: the adapter cannot know whether
                        // anything changed while it was disconnected, and the reload is lazy.
                        await FlushCkModelCacheAfterReconnectAsync();

                        // Handle interrupted executions after reconnect
                        await HandleInterruptedExecutionsAsync();

                        _logger.Info("Sending deployment result to adapter hub");
                        await _hubClient.SendDeploymentUpdateResultAsync(rtEntityId,
                            new DeploymentResult { IsSuccess = true, ErrorMessages = deploymentErrorMessages });
                        _logger.Info("Deployment result sent to adapter hub");
                    }
                }
                catch (ObjectDisposedException)
                {
                    _registrationState.MarkNotRegistered("hub connection was disposed during reconnect");
                    _logger.Warn("Hub connection was disposed during reconnect, skipping error report");
                }
                catch (Exception e)
                {
                    _registrationState.MarkNotRegistered(e.Message);
                    _logger.Error(e, "Error during reconnect of adapter");

                    try
                    {
                        var rtEntityId = GetAdapterRtEntityId();
                        await _hubClient.SendDeploymentUpdateResultAsync(rtEntityId,
                            new DeploymentResult
                            {
                                IsSuccess = false,
                                ErrorMessages =
                                [
                                    new DeploymentUpdateErrorMessageDto
                                    {
                                        ErrorCategory = DeploymentErrorCategories.Uncategorized,
                                        ErrorMessage = e.Message
                                    }
                                ]
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to send deployment error result during reconnect");
                    }

                    // Rethrow after the best-effort error report: swallowing the failure made the
                    // SignalR (re)connect loop treat a failed registration as success and exit —
                    // the adapter then sat unregistered on a dead control plane forever (AB#4805).
                    // The loop in SignalRClient catches this and retries.
                    throw;
                }
            }

            await StartCommunicationAsync(cancellationToken, ReConnectFunction);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error during initialization of adapter execution service");

            try
            {
                var rtEntityId = GetAdapterRtEntityId();
                await _hubClient.SendDeploymentUpdateResultAsync(rtEntityId,
                    new DeploymentResult
                    {
                        IsSuccess = false,
                        ErrorMessages =
                        [
                            new DeploymentUpdateErrorMessageDto
                                { ErrorCategory = DeploymentErrorCategories.Uncategorized, ErrorMessage = e.Message }
                        ]
                    });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to send deployment error result during initialization");
            }

            throw;
        }
    }

    /// <summary>
    /// Stops the adapter execution service.
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = _adapterOptions.Value.DedicatedTenantId;
            if (string.IsNullOrWhiteSpace(tenantId) || tenantId == null)
            {
                return;
            }

            // Serialize the shutdown with configuration updates: the nightly PreUpdateTenant
            // restart used to run UnregisterAllPipelines concurrently with a selective pipeline
            // update on the same registry and hung forever (AB#4876). Best effort — after the
            // timeout we proceed, a bounded race beats an unbounded freeze.
            var lockTaken =
                await _configurationUpdateLock.WaitAsync(ConfigurationUpdateLockTimeout, CancellationToken.None);
            if (!lockTaken)
            {
                _logger.Warn(
                    "Timed out after {Timeout} waiting for the configuration update lock before adapter shutdown, proceeding without it",
                    ConfigurationUpdateLockTimeout);
            }

            try
            {
                await ShutdownAdapterBoundedAsync(tenantId, cancellationToken);
            }
            finally
            {
                if (lockTaken)
                {
                    _configurationUpdateLock.Release();
                }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_adapterOptions.Value.AdapterRtId))
                {
                    var rtEntityId = GetAdapterRtEntityId();
                    await _hubClient.UnRegisterAdapterAsync(rtEntityId).WaitAsync(HubInvokeTimeout);
                }
            }
            catch (Exception e)
            {
                // The communication controller may have already removed the adapter from its cache
                // (e.g. during a tenant update). Log and continue to ensure the hub client is stopped.
                _logger.Warn(e, "Failed to unregister adapter from hub, continuing with hub client shutdown");
            }

            await _hubClient.StopAsync();
            _registrationState.MarkNotRegistered("adapter execution service stopped");
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error during deinitialization of adapter execution service");
        }
    }

    /// <summary>
    /// Runs <see cref="IAdapterService.ShutdownAsync"/> bounded by <see cref="AdapterShutdownTimeout"/>.
    /// On timeout the stuck shutdown keeps running in the background and the caller proceeds —
    /// the alternative was a restart sequence frozen for days with the hub connection dead (AB#4876).
    /// </summary>
    private async Task ShutdownAdapterBoundedAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _adapterService.ShutdownAsync(new AdapterShutdown { TenantId = tenantId }, cancellationToken)
                .WaitAsync(AdapterShutdownTimeout);
        }
        catch (TimeoutException)
        {
            _logger.Error(
                "Adapter shutdown did not complete within {Timeout}, proceeding anyway; the stuck shutdown keeps running in the background",
                AdapterShutdownTimeout);
        }
    }

    private RtEntityId GetAdapterRtEntityId()
    {
        if (!string.IsNullOrWhiteSpace(_adapterOptions.Value.AdapterRtId) &&
            !string.IsNullOrWhiteSpace(_adapterOptions.Value.AdapterCkTypeId))
        {
            var adapterRtId = OctoObjectId.Parse(_adapterOptions.Value.AdapterRtId!);
            var adapterCkId = new RtCkId<CkTypeId>(_adapterOptions.Value.AdapterCkTypeId!);
            var rtEntityId = new RtEntityId(adapterCkId, adapterRtId);
            return rtEntityId;
        }

        throw AdapterException.ConfigurationErrorAdapterRtIdAdapterCkTypeIdNotSet();
    }

    private async Task StartCommunicationAsync(CancellationToken stoppingToken,
        Func<bool, Task> onReconnectFunc)
    {
        _logger.Info("Starting adapter...");
        _logger.Info("Connecting to adapter hub at {CommunicationControllerServicesUri}",
            _adapterOptions.Value.CommunicationControllerServicesUri);
        _logger.Info("TenantId {TenantId}, AdapterRtId {AdapterRtId}",
            _adapterOptions.Value.DedicatedTenantId, _adapterOptions.Value.AdapterRtId);

        if (_adapterOptions.Value.AdapterRtId == null)
        {
            _logger.Error("AdapterRtId is null");
            return;
        }

        await _hubClient.StartAsync(onReconnectFunc, stoppingToken);
        _logger.Info("Connected to adapter hub");

        if (stoppingToken.IsCancellationRequested)
        {
            await _hubClient.StopAsync();
            return;
        }

        _logger.Info("Enabling automatic reconnect");
        _hubClient.EnableReconnect(onReconnectFunc);
    }

    /// <summary>
    ///     Drops the tenant's in-process CK model cache after the hub connection was re-established
    ///     (AB#5415), because <see cref="IAdapterHubCallbacks.CkModelChangedAsync" /> is only
    ///     delivered to adapters that were connected at the moment it was broadcast. Best-effort:
    ///     the cache reloads lazily on the next execution, so a failure here must not fail the
    ///     reconnect - it only leaves the adapter where it already was.
    /// </summary>
    private async Task FlushCkModelCacheAfterReconnectAsync()
    {
        var tenantId = _adapterOptions.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        try
        {
            _logger.Info(
                "Invalidating CK model cache for tenant {TenantId} after reconnect, CK model changes announced while disconnected were not delivered",
                tenantId);
            await _adapterService.CkModelChangedAsync(tenantId);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Invalidating the CK model cache after reconnect failed for tenant {TenantId}", tenantId);
        }
    }

    /// <summary>
    /// Handles interrupted executions after adapter reconnects.
    /// Queries the server for executions that were marked as interrupted and reports their final status.
    /// </summary>
    private async Task HandleInterruptedExecutionsAsync()
    {
        if (_executionReporter == null)
        {
            return;
        }

        try
        {
            _logger.Info("Checking for interrupted executions...");
            var interruptedIds = await _executionReporter.GetInterruptedExecutionIdsAsync();

            if (interruptedIds.Count == 0)
            {
                _logger.Info("No interrupted executions found");
                return;
            }

            _logger.Info("Found {Count} interrupted executions to report", interruptedIds.Count);

            var tenantId = _adapterOptions.Value.DedicatedTenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return;
            }

            foreach (var executionIdString in interruptedIds)
            {
                try
                {
                    if (!Guid.TryParse(executionIdString, out var executionId))
                    {
                        _logger.Warn("Invalid execution ID format: {ExecutionId}", executionIdString);
                        continue;
                    }

                    // Try to find the execution in local pipeline registrations
                    var (status, startTime) = GetLocalExecutionStatus(tenantId!, executionId);

                    var completedAt = DateTime.UtcNow;
                    var durationMs = startTime.HasValue
                        ? (int)(completedAt - startTime.Value).TotalMilliseconds
                        : 0;

                    await _executionReporter.ReportInterruptedExecutionResultAsync(
                        executionId,
                        status,
                        completedAt,
                        durationMs,
                        status == PipelineExecutionStatus.Failed ? "Execution failed during disconnect" : null);

                    _logger.Info("Reported final status {Status} for interrupted execution {ExecutionId}",
                        status, executionId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to report interrupted execution {ExecutionId}", executionIdString);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error handling interrupted executions");
        }
    }

    /// <summary>
    /// Gets the local execution status for an execution ID.
    /// </summary>
    private (PipelineExecutionStatus status, DateTime? startTime) GetLocalExecutionStatus(string tenantId, Guid executionId)
    {
        // Check all registered pipelines for this execution
        foreach (var pipelineRtEntityId in _pipelineRegistryService.GetRegisteredPipelines(tenantId))
        {
            if (_pipelineRegistryService.TryGetPipelineRegistration(tenantId, pipelineRtEntityId,
                    out var registration) && registration != null)
            {
                var status = registration.GetPipelineExecutionStatus(executionId);
                var startTime = registration.GetExecutionStartTime(executionId);

                // If we found a non-failed status, or if we found the execution at all
                if (status != PipelineExecutionStatus.Failed || startTime.HasValue)
                {
                    return (status, startTime);
                }
            }
        }

        // Execution not found locally. The task is gone (process restarted, or it was evicted
        // from the bounded in-memory registry) and its true outcome is unknown. Report Failed
        // rather than optimistically Completed: an execution that was interrupted mid-flight must
        // never be recorded as a success (AB#4280).
        return (PipelineExecutionStatus.Failed, null);
    }

    // AB#4924: shared with AdapterPoolClient - the pool member reports the same descriptors on its
    // own registration, and two projections would be two answers to one question.
    private IReadOnlyList<NodeDescriptorDto>? GetNodeDescriptorDtos()
    {
        return AdapterNodeDescriptorProjection.TryProject(_nodeSchemaRegistry,
            e => _logger.Warn(e, "Failed to generate node descriptors, registering without them"));
    }

    private string? GetPipelineSchemaJson()
    {
        return AdapterNodeDescriptorProjection.TryGenerateSchema(_pipelineSchemaGenerator,
            e => _logger.Warn(e, "Failed to generate pipeline schema, registering without it"));
    }
}