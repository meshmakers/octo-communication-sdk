using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Keeps the adapter's own service credential — the access token every
///     <c>/{tenantId}/adapterHub</c> connection presents — current for the lifetime of the process
///     (AB#5072).
/// </summary>
/// <remarks>
///     <para>
///         The token is written into the process-wide <see cref="IServiceClientAccessToken" /> that
///         the adapter builders hand to <c>AdapterHubClient</c>. The SDK reads that object through
///         <c>HttpConnectionOptions.AccessTokenProvider</c> on <b>every</b> connection attempt, so a
///         token refreshed here is picked up by the next (re)connect without anything having to
///         notify the client.
///     </para>
///     <para>
///         <b>Why this service exists at all.</b> Nothing filled that holder at startup. The only
///         production writer was <c>ServiceAccountTokenService.EnsureTokenAsync</c> in
///         <c>octo-mesh-adapter</c>, and every one of its callers sits on a pipeline
///         <i>execution</i> path (<c>DeployPipeline@1</c>, <c>AnthropicAiQuery@1</c>,
///         <c>MeshContextCreatorService</c>, <c>PipelineIdentityResolver</c>). So the holder was
///         empty at connect time, the provider returned <c>null</c>, the connection went out with no
///         <c>Authorization</c> header and the hub saw an anonymous caller that identified itself
///         only through the unprotected <c>adapter-rtId</c> / <c>adapter-ckTypeId</c> headers. Worse
///         than plainly anonymous: an adapter that had already run one of those pipelines <i>did</i>
///         present a token on its <b>next</b> reconnect, so the fleet's state was not deterministic
///         and the adapter-hub gate's (AB#5063) <c>LogOnly</c> inventory was not worth reading.
///     </para>
///     <para>
///         <b>Why a refresh loop is needed even though the connection survives expiry.</b> An
///         established SignalR connection is authorized once, at connect time. The exposure is the
///         <b>re</b>connect — and an adapter reconnects routinely (controller rollout, node drain,
///         network blip, the SDK's own retry loop, a wake from scale-to-zero). An adapter that
///         acquired one token at startup would reconnect days later with a long-expired one and,
///         under <c>Enforce</c>, be refused permanently.
///     </para>
///     <para>
///         Acquisition happens in <see cref="StartAsync" />, before the base class starts the loop.
///         Hosted services are started sequentially, so registering this service before
///         <c>HostedAdapterExecutionService</c> means the first hub connection already carries a
///         token instead of racing the first acquisition.
///     </para>
///     <para>
///         Every failure is logged and swallowed. Refusing to start would turn a temporarily
///         unreachable identity service into an adapter outage, and the connection itself is still
///         valuable while the controller-side gate observes rather than enforces.
///     </para>
/// </remarks>
public sealed class AdapterAccessTokenService : BackgroundService
{
    /// <summary>
    ///     How long before its own expiry a token is replaced. Comfortably longer than a token
    ///     request plus a reconnect, so a connection attempt never picks up a token that dies in
    ///     flight.
    /// </summary>
    public static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Floor for the sleep between refresh attempts. Also the cadence after a failed
    ///     acquisition, so an adapter that came up before the identity service recovers on its own.
    /// </summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceClientAccessToken _accessToken;
    private readonly IAuthenticatorClient _authenticatorClient;
    private readonly ILogger<AdapterAccessTokenService> _logger;
    private readonly AdapterOptions _options;

    private DateTime _expiresAtUtc = DateTime.MinValue;

    /// <summary>
    ///     Constructor.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="options">The adapter options carrying the client-credentials configuration.</param>
    /// <param name="accessToken">The process-wide access-token holder the SignalR client reads.</param>
    /// <param name="authenticatorClient">The SDK authenticator client running the grant.</param>
    public AdapterAccessTokenService(
        ILogger<AdapterAccessTokenService> logger,
        IOptions<AdapterOptions> options,
        IServiceClientAccessToken accessToken,
        IAuthenticatorClient authenticatorClient)
    {
        _logger = logger;
        _options = options.Value;
        _accessToken = accessToken;
        _authenticatorClient = authenticatorClient;
    }

    /// <summary>Test seam: the delay between refresh attempts is driven at millisecond speed.</summary>
    internal TimeSpan RetryIntervalOverride { get; set; } = RetryInterval;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled)
        {
            // One warning, at startup, naming the consequence rather than the missing key — this is
            // the line an operator reads when the controller-side inventory (AB#5063 LogOnly) shows
            // their adapter as anonymous.
            _logger.LogWarning(
                "No Adapter:ClientId / Adapter:IssuerUri configured. The adapter connects to the communication " +
                "controller's adapter hub without an access token, exactly as before, and identifies itself only " +
                "through the unprotected adapter-rtId / adapter-ckTypeId headers. The controller's adapter-hub " +
                "authorization (AB#5063) must stay in LogOnly for this installation");
            await base.StartAsync(cancellationToken);
            return;
        }

        await EnsureTokenAsync();
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(NextDelay(), stoppingToken);
                await EnsureTokenAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // 🔴 Nothing may escape this loop (AB#5080). HostOptions.BackgroundServiceException-
                // Behavior defaults to StopHost, so a single throw here does not degrade the token
                // refresh — it takes the whole adapter down and leaves it crash-looping. This service
                // is a helper: losing it must mean the adapter falls back to an anonymous hub
                // connection, which is exactly how every adapter in the estate runs today, and never
                // that the adapter stops running.
                _logger.LogError(ex,
                    "Unexpected failure in the adapter access-token loop for client {ClientId}; the " +
                    "adapter keeps running with whatever token it already has (possibly none) and the " +
                    "next attempt runs in {RetryInterval}", _options.ClientId, RetryIntervalOverride);

                try
                {
                    await Task.Delay(RetryIntervalOverride, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    ///     Time to sleep before the next attempt: until shortly before the current token expires, or
    ///     the retry cadence when there is no usable token (never acquired, or the last attempt
    ///     failed). Never shorter than the retry cadence, so a pathologically short-lived token
    ///     cannot turn this into a hot loop against the identity service.
    /// </summary>
    private TimeSpan NextDelay()
    {
        // 🔴 Guard the arithmetic, not just its result (AB#5080). _expiresAtUtc is DateTime.MinValue
        // until the first successful acquisition, and DateTime.MinValue - RefreshSkew underflows and
        // throws ArgumentOutOfRangeException *before* the comparison below can pick the retry
        // cadence — so the one case this method exists to handle was the one that crashed. Since an
        // unhandled BackgroundService exception stops the host by default, that turned "the identity
        // service refused the token" into a crash loop for the entire adapter. Found on the local
        // kind cluster; every unit test had a token, so none of them reached this line.
        if (_expiresAtUtc - DateTime.MinValue <= RefreshSkew)
        {
            return RetryIntervalOverride;
        }

        var untilRefresh = _expiresAtUtc - RefreshSkew - DateTime.UtcNow;
        return untilRefresh > RetryIntervalOverride ? untilRefresh : RetryIntervalOverride;
    }

    /// <summary>
    ///     Acquires a token unless the current one is still comfortably valid. Returns whether a
    ///     usable token is in place afterwards.
    /// </summary>
    internal async Task<bool> EnsureTokenAsync()
    {
        if (!_options.IsEnabled)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_accessToken.AccessToken) && _expiresAtUtc > DateTime.UtcNow + RefreshSkew)
        {
            return true;
        }

        try
        {
            // DefaultScopes.None: exactly "octo_api", which is what the controller's adapter-hub
            // policy requires. Notably WITHOUT offline_access - a client-credentials grant has no
            // refresh token by design, and re-running the grant is both cheaper and the only way a
            // revoked client stops being able to connect.
            // acr_values=tenant:{TenantId} is appended by AuthenticatorClient from the configured
            // AuthenticatorOptions.TenantId; see ConfigureAdapterAuthenticatorOptions. Without it
            // the identity service issues for the SYSTEM tenant (AB#5077) and the controller then
            // refuses the adapter on its own tenant route with a 403.
            var authenticationData = await _authenticatorClient.RequestClientCredentialsTokenAsync(
                ApiScopes.OctoApiFullAccess, DefaultScopes.None);

            if (string.IsNullOrWhiteSpace(authenticationData.AccessToken))
            {
                _logger.LogError(
                    "The identity service at {IssuerUri} accepted the adapter's client-credentials request for " +
                    "client {ClientId} but returned no access token",
                    _options.IssuerUri, _options.ClientId);
                return false;
            }

            // AuthenticatorClient builds ExpiresAt from DateTime.Now (local kind). Deriving the
            // remaining lifetime and re-basing it on UtcNow keeps this correct regardless of the
            // kind the SDK happens to use - comparing the two directly would be off by the local
            // offset, which on a CEST cluster means acting on a token two hours after it died.
            var lifetime = authenticationData.ExpiresAt - DateTime.Now;
            _expiresAtUtc = DateTime.UtcNow + (lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero);
            _accessToken.AccessToken = authenticationData.AccessToken;

            _logger.LogInformation(
                "Adapter access token acquired for client {ClientId} in tenant '{TenantId}', expires at {ExpiresAtUtc:O}",
                _options.ClientId, _options.TenantId ?? string.Empty, _expiresAtUtc);
            return true;
        }
        catch (Exception e)
        {
            // The previously acquired token is deliberately left in place. It is no less usable than
            // no token at all, and dropping it would guarantee a refusal on the next reconnect for a
            // failure that is usually a transient identity-service blip.
            _logger.LogError(e,
                "Could not acquire an access token for client {ClientId} from {IssuerUri}; the adapter hub " +
                "connection keeps using the previous token (if any) and the next attempt runs in {RetryInterval}",
                _options.ClientId, _options.IssuerUri, RetryIntervalOverride);
            return false;
        }
    }
}
