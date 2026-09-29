using Meshmakers.Octo.Sdk.Common.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sdk.Common.Tests.Adapters;

/// <summary>
///     AB#5303 item 4 — a workload pointed at datastores that do not exist must say so at startup.
/// </summary>
/// <remarks>
///     🔴 The failure being replaced was invisible for as long as a member went unleased: nothing in
///     a pool member's startup touches MongoDB, so a wrong host surfaced only inside
///     <c>EnterLeaseAsync</c>, in a stack trace naming neither the setting nor the value.
/// </remarks>
public class DatastoreHostPreflightTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();
    }

    private static DatastoreHostPreflight Create(IConfiguration configuration,
        params string[] resolvableHosts)
    {
        var resolvable = new HashSet<string>(resolvableHosts, StringComparer.OrdinalIgnoreCase);

        return new DatastoreHostPreflight(configuration, NullLogger<DatastoreHostPreflight>.Instance)
        {
            Attempts = 2,
            RetryDelay = TimeSpan.Zero,
            ResolveAsync = (host, _) => resolvable.Contains(host)
                ? Task.CompletedTask
                : throw new InvalidOperationException($"no such host: {host}")
        };
    }

    /// <summary>The measured case: the operator handed hosts from namespaces that do not exist.</summary>
    [Fact]
    public async Task AnUnresolvableHostStopsTheHostAndNamesTheKey()
    {
        var configuration = Configuration(
            ("System:DatabaseHost", "octo-mongodb-0.octo-mongodb-svc.mongodb.svc.cluster.local:27017"),
            ("Adapter:BrokerHost", "rabbitmq.octo-infra.svc.cluster.local"));

        var sut = Create(configuration, "rabbitmq.octo-infra.svc.cluster.local");

        var exception = await Assert.ThrowsAsync<AdapterException>(
            () => sut.StartAsync(TestContext.Current.CancellationToken));

        // The key, because the value alone does not tell an operator where to change it.
        Assert.Contains("System:DatabaseHost", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mongodb.svc.cluster.local", exception.Message, StringComparison.Ordinal);
        // The one that did resolve must not be blamed.
        Assert.DoesNotContain("Adapter:BrokerHost", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Every bad host in one message — an operator fixing one at a time is a slow loop.</summary>
    [Fact]
    public async Task AllUnresolvableHostsAreReportedTogether()
    {
        var configuration = Configuration(
            ("System:DatabaseHost", "mongo-a:27017,mongo-b:27017"),
            ("Adapter:BrokerHost", "rabbit"));

        var sut = Create(configuration);

        var exception = await Assert.ThrowsAsync<AdapterException>(
            () => sut.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("mongo-a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mongo-b", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rabbit", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>MongoDB arrives as a comma-separated host:port list; each entry is its own host.</summary>
    [Fact]
    public async Task EveryHostOfAReplicaSetListIsChecked()
    {
        var configuration = Configuration(
            ("System:DatabaseHost", "mongo-0.svc:27017, mongo-1.svc:27017 ,mongo-2.svc:27017"));

        var sut = Create(configuration, "mongo-0.svc", "mongo-1.svc", "mongo-2.svc");

        await sut.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     🔴 CrateDB is opt-in (StreamData:Enabled defaults to false) and the chart renders its host
    ///     either way. Checking it while the feature is off would fail adapters that never touch it.
    /// </summary>
    [Fact]
    public async Task TheStreamDataHostIsIgnoredWhileStreamDataIsOff()
    {
        var configuration = Configuration(
            ("Adapter:StreamDataHost", "crate-octo-crate.cratedb.svc.cluster.local"));

        var sut = Create(configuration);

        await sut.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheStreamDataHostIsCheckedOnceStreamDataIsOn()
    {
        var configuration = Configuration(
            ("StreamData:Enabled", "true"),
            ("Adapter:StreamDataHost", "crate-octo-crate.cratedb.svc.cluster.local"));

        var sut = Create(configuration);

        var exception = await Assert.ThrowsAsync<AdapterException>(
            () => sut.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Adapter:StreamDataHost", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A literal address is not a naming mistake and needs no resolver.</summary>
    [Theory]
    [InlineData("127.0.0.1:27017")]
    [InlineData("10.244.0.12")]
    [InlineData("[::1]:27017")]
    public async Task LiteralAddressesPassWithoutResolution(string value)
    {
        var sut = Create(Configuration(("System:DatabaseHost", value)));

        await sut.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Nothing configured is not a failure: a host that names no datastore has none to get wrong,
    ///     and the check must never be the reason such a process cannot start.
    /// </summary>
    [Fact]
    public async Task NoConfigurationIsNotAFailure()
    {
        var sut = Create(new ConfigurationBuilder().Build());

        await sut.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     🔴 The retry exists because a pod can start before CoreDNS answers for its services. The
    ///     check must not turn that race into a crash loop, so a host that resolves on a later
    ///     attempt is accepted.
    /// </summary>
    [Fact]
    public async Task AHostThatResolvesOnARetryIsAccepted()
    {
        var attempts = 0;
        var sut = new DatastoreHostPreflight(
            Configuration(("Adapter:BrokerHost", "rabbitmq")),
            NullLogger<DatastoreHostPreflight>.Instance)
        {
            Attempts = 3,
            RetryDelay = TimeSpan.Zero,
            ResolveAsync = (_, _) => ++attempts < 3
                ? throw new InvalidOperationException("not yet")
                : Task.CompletedTask
        };

        await sut.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, attempts);
    }
}
