using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5072 — the adapter must present a real access token on the controller's adapter hub when
///     it is given credentials, and must behave exactly as it always has when it is not.
/// </summary>
public class AdapterAccessTokenServiceTests
{
    private const string IssuerUri = "https://connect.test-2.mm.cloud";
    private const string ClientId = "octo-mesh-adapter";
    private const string ClientSecret = "s3cr3t-never-logged";
    private const string TenantId = "testTenant";

    private static AdapterOptions ConfiguredOptions() =>
        new()
        {
            TenantId = TenantId,
            IssuerUri = IssuerUri,
            ClientId = ClientId,
            ClientSecret = ClientSecret
        };

    private static AdapterAccessTokenService CreateService(
        IAuthenticatorClient authenticatorClient,
        IServiceClientAccessToken accessToken,
        AdapterOptions? options = null,
        ILogger<AdapterAccessTokenService>? logger = null) =>
        new(
            logger ?? NullLogger<AdapterAccessTokenService>.Instance,
            Options.Create(options ?? ConfiguredOptions()),
            accessToken,
            authenticatorClient);

    private static void ReturnsToken(IAuthenticatorClient authenticatorClient, string token, TimeSpan lifetime)
    {
        // AuthenticatorClient computes ExpiresAt from DateTime.Now, so the fake mirrors that kind —
        // the service is expected to convert via the remaining lifetime rather than compare kinds.
        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .Returns(new AuthenticationData { AccessToken = token, ExpiresAt = DateTime.Now + lifetime });
    }

    [Fact]
    public async Task ConfiguredAdapter_PublishesTheTokenIntoTheSharedAccessToken()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        ReturnsToken(authenticatorClient, "adapter-token", TimeSpan.FromHours(1));
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken);

        var acquired = await service.EnsureTokenAsync();

        Assert.True(acquired);
        Assert.Equal("adapter-token", accessToken.AccessToken);
    }

    [Fact]
    public async Task ConfiguredAdapter_RequestsOctoApiFullAccessWithoutOfflineAccess()
    {
        // octo_api is what the controller's adapter-hub policy requires; offline_access would ask
        // for a refresh token a client-credentials grant does not issue.
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        ReturnsToken(authenticatorClient, "adapter-token", TimeSpan.FromHours(1));
        using var service = CreateService(authenticatorClient, new ServiceClientAccessToken());

        await service.EnsureTokenAsync();

        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                ApiScopes.OctoApiFullAccess, DefaultScopes.None, A<IEnumerable<string>?>._, A<string?>._,
                A<string?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UnconfiguredAdapter_NeverRequestsAToken_AndConnectsAnonymously()
    {
        // The compatibility guarantee: every adapter in the estate runs without these keys today,
        // and must keep connecting exactly as before.
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken,
            new AdapterOptions { TenantId = TenantId });

        var acquired = await service.EnsureTokenAsync();

        Assert.False(acquired);
        Assert.Null(accessToken.AccessToken);
        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task IssuerUriWithoutClientId_CountsAsUnconfigured()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        using var service = CreateService(authenticatorClient, new ServiceClientAccessToken(),
            new AdapterOptions { TenantId = TenantId, IssuerUri = IssuerUri });

        Assert.False(await service.EnsureTokenAsync());
        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ClientIdWithoutIssuerUri_CountsAsUnconfigured()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        using var service = CreateService(authenticatorClient, new ServiceClientAccessToken(),
            new AdapterOptions { TenantId = TenantId, ClientId = ClientId, ClientSecret = ClientSecret });

        Assert.False(await service.EnsureTokenAsync());
        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task AValidTokenIsNotReAcquired()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        ReturnsToken(authenticatorClient, "adapter-token", TimeSpan.FromHours(1));
        using var service = CreateService(authenticatorClient, new ServiceClientAccessToken());

        await service.EnsureTokenAsync();
        await service.EnsureTokenAsync();

        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ATokenInsideTheRefreshWindowIsReplaced()
    {
        // The reconnect is the exposure: an adapter that keeps a near-expired token would present it
        // on the next reconnect and be refused for good once the gate enforces.
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken);

        ReturnsToken(authenticatorClient, "first-token",
            AdapterAccessTokenService.RefreshSkew - TimeSpan.FromMinutes(1));
        await service.EnsureTokenAsync();
        Assert.Equal("first-token", accessToken.AccessToken);

        ReturnsToken(authenticatorClient, "second-token", TimeSpan.FromHours(1));
        await service.EnsureTokenAsync();

        Assert.Equal("second-token", accessToken.AccessToken);
    }

    [Fact]
    public async Task AFailedAcquisitionKeepsThePreviousTokenAndDoesNotThrow()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken);

        ReturnsToken(authenticatorClient, "first-token",
            AdapterAccessTokenService.RefreshSkew - TimeSpan.FromMinutes(1));
        await service.EnsureTokenAsync();

        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .ThrowsAsync(new HttpRequestException("identity service unreachable"));

        var acquired = await service.EnsureTokenAsync();

        Assert.False(acquired);
        Assert.Equal("first-token", accessToken.AccessToken);
    }

    [Fact]
    public async Task ATokenlessResponseIsNotPublished()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
            .Returns(new AuthenticationData { AccessToken = null, ExpiresAt = DateTime.Now.AddHours(1) });
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken);

        var acquired = await service.EnsureTokenAsync();

        Assert.False(acquired);
        Assert.Null(accessToken.AccessToken);
    }

    [Fact]
    public async Task StartAsync_AcquiresTheTokenBeforeItReturns()
    {
        // Hosted services start sequentially, so completing acquisition inside StartAsync is what
        // guarantees the first hub connection already carries a token instead of racing it.
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        ReturnsToken(authenticatorClient, "adapter-token", TimeSpan.FromHours(1));
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal("adapter-token", accessToken.AccessToken);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_OnAnUnconfiguredAdapter_StartsCleanlyWithoutAToken()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken,
            new AdapterOptions { TenantId = TenantId });

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.Null(accessToken.AccessToken);
            A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                    A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
                .MustNotHaveHappened();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task NeitherTheClientSecretNorTheTokenReachesTheRenderedLog()
    {
        // Asserted on the RENDERED message, not on the format string: a placeholder added to the
        // argument list is what would leak, and a format-string check would never see it.
        var capturingLogger = new CapturingLogger();
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        var accessToken = new ServiceClientAccessToken();
        using var service = CreateService(authenticatorClient, accessToken, logger: capturingLogger);

        ReturnsToken(authenticatorClient, "adapter-token", TimeSpan.FromHours(1));
        await service.StartAsync(CancellationToken.None);
        try
        {
            // …and again on the failure path, which is the one that logs the most context.
            A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                    A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>?>._, A<string?>._, A<string?>._))
                .ThrowsAsync(new HttpRequestException("identity service unreachable"));
            accessToken.AccessToken = null;
            await service.EnsureTokenAsync();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.NotEmpty(capturingLogger.Messages);
        Assert.DoesNotContain(capturingLogger.Messages, m => m.Contains(ClientSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(capturingLogger.Messages, m => m.Contains("adapter-token", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Captures the <b>rendered</b> log lines — the format string with its arguments already
    ///     substituted, which is what a secret passed as a log argument would surface in. The
    ///     exception the service is handed is deliberately not part of the captured text: whether a
    ///     foreign exception carries credential material is not this service's contract, and folding
    ///     it in would make the assertion pass or fail for the wrong reason.
    /// </summary>

    /// <summary>
    ///     🔴 AB#5080 — the regression this whole class missed. Every other test here hands the
    ///     service a token first, so none of them ever reached the delay computation in its
    ///     no-token-yet state: <c>_expiresAtUtc</c> is <see cref="DateTime.MinValue" /> until the
    ///     first success, and subtracting the refresh skew from it underflows and throws
    ///     <see cref="ArgumentOutOfRangeException" /> — in the one situation the code exists to
    ///     survive. Because an unhandled <see cref="BackgroundService" /> exception stops the host by
    ///     default, that turned a refused token into a crash-looping adapter. Found on the local kind
    ///     cluster, not here.
    /// </summary>
    [Fact]
    public async Task AcquisitionFailureWithNoPreviousToken_KeepsTheServiceRunning()
    {
        var authenticatorClient = A.Fake<IAuthenticatorClient>();
        A.CallTo(() => authenticatorClient.RequestClientCredentialsTokenAsync(
                A<ApiScopes>._, A<DefaultScopes>._, A<IEnumerable<string>>._, A<string>._, A<string>._))
            .Throws(new InvalidOperationException("Issuer name does not match authority"));

        var accessToken = new ServiceClientAccessToken();
        var logger = new CapturingLogger();
        var service = CreateService(authenticatorClient, accessToken, logger: logger);
        service.RetryIntervalOverride = TimeSpan.FromMilliseconds(20);

        // StartAsync does the first acquisition and then starts the refresh loop. Neither may throw:
        // the acquisition failure is expected here, and the loop's delay computation is what used to
        // blow up on the resulting empty state.
        await service.StartAsync(CancellationToken.None);

        // Give the loop a moment to reach its first NextDelay() and come back around.
        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

        // ExecuteTask is the BackgroundService's own task -- a faulted one is precisely what makes the
        // host stop, so asserting on it is asserting on the crash loop itself.
        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask!.IsFaulted,
            "the refresh loop faulted; an unhandled exception here stops the host and crash-loops the adapter");
        Assert.Null(accessToken.AccessToken);

        // 🔴 The discriminating assertion. The catch-all in ExecuteAsync keeps the host alive even
        // when NextDelay() throws, so "the loop did not fault" alone passes with the arithmetic bug
        // still in place -- it only proves the safety net. The safety net announces itself, so its
        // silence is what proves the arithmetic is right: with the underflow present this line is
        // logged on every iteration.
        Assert.DoesNotContain(logger.Messages,
            m => m.Contains("Unexpected failure in the adapter access-token loop"));

        await service.StopAsync(CancellationToken.None);
    }

    private sealed class CapturingLogger : ILogger<AdapterAccessTokenService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
