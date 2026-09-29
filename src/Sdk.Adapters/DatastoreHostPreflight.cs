using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Refuses to start an adapter whose configured datastore hostnames do not resolve
///     (AB#5303 item 4).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>The misconfiguration this exists for is invisible until a lease is granted.</b> A
///         pool member has no tenant of its own, so nothing in its startup path touches MongoDB: it
///         comes up, registers with its pool, reports healthy, and dies inside
///         <c>CkModelCacheLeaseParticipant.EnterLeaseAsync</c> on a DNS failure the first time it is
///         leased — minutes or hours later, in a stack trace that names neither the setting nor the
///         value. Observed on a local kind cluster, where a workload had been handed
///         <c>…mongodb.svc.cluster.local</c> while the services in that cluster live in
///         <c>octo-infra</c>.
///     </para>
///     <para>
///         🔴 <b>It resolves, it does not connect</b> — and that distinction is the whole design. A
///         Kubernetes Service's DNS name resolves while its pods are down, restarting or not yet
///         scheduled, so resolution tests the <i>configuration</i> and not the liveness of the
///         database. A connectivity probe here would turn every database restart into a crash loop
///         of every adapter in the cluster, which is a worse failure than the one being fixed.
///     </para>
///     <para>
///         <b>Why <see cref="IHostedService" /> and not a background service.</b> A throwing
///         <c>StartAsync</c> aborts host startup, which is exactly what is wanted: the pod exits with
///         the reason in its log and Kubernetes shows CrashLoopBackOff. That is not the hazard AB#5080
///         guarded against — there, an exception out of a long-running <c>ExecuteAsync</c> tore down a
///         host that was already serving work.
///     </para>
/// </remarks>
public class DatastoreHostPreflight : IHostedService
{
    /// <summary>Configuration keys checked, in the order an operator would read them.</summary>
    private static readonly string[] HostKeys = ["System:DatabaseHost", "Adapter:BrokerHost"];

    private const string StreamDataHostKey = "Adapter:StreamDataHost";
    private const string StreamDataEnabledKey = "StreamData:Enabled";

    private readonly IConfiguration _configuration;
    private readonly ILogger<DatastoreHostPreflight> _logger;

    /// <summary>Constructor.</summary>
    public DatastoreHostPreflight(IConfiguration configuration, ILogger<DatastoreHostPreflight> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Attempts, and the pause between them. A pod can start before CoreDNS answers for it.</summary>
    internal int Attempts { get; init; } = 5;

    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Indirection for the test suite; production resolves through DNS.</summary>
    internal Func<string, CancellationToken, Task> ResolveAsync { get; init; } =
        static async (host, cancellationToken) => await Dns.GetHostEntryAsync(host, cancellationToken);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var targets = CollectTargets().ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        var unresolved = new List<string>();

        foreach (var (key, host) in targets)
        {
            if (await CanResolveAsync(host, cancellationToken))
            {
                _logger.LogDebug("Datastore host '{Host}' ({Key}) resolves", host, key);
            }
            else
            {
                unresolved.Add($"{key} = '{host}'");
            }
        }

        if (unresolved.Count == 0)
        {
            return;
        }

        throw AdapterException.DatastoreHostsDoNotResolve(unresolved);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private IEnumerable<(string Key, string Host)> CollectTargets()
    {
        var keys = new List<string>(HostKeys);

        // CrateDB is an opt-in stack (StreamDataInstanceConfiguration.Enabled defaults to false), and
        // the chart renders its host either way. Checking it while the feature is off would fail
        // adapters that never touch it.
        if (_configuration.GetValue(StreamDataEnabledKey, false))
        {
            keys.Add(StreamDataHostKey);
        }

        foreach (var key in keys)
        {
            var value = _configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // MongoDB arrives as a comma-separated host:port list; the other two are bare hosts.
            // Splitting on both handles all three without knowing which is which.
            foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var host = StripPort(entry);
                if (!string.IsNullOrWhiteSpace(host))
                {
                    yield return (key, host);
                }
            }
        }
    }

    /// <summary>
    ///     Drops a trailing <c>:port</c>. Bracketed IPv6 literals keep their brackets stripped too;
    ///     an unbracketed IPv6 literal has many colons and is left alone, because splitting it would
    ///     produce a host that is worse than the original.
    /// </summary>
    private static string StripPort(string entry)
    {
        var value = entry.Trim();

        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            return close > 1 ? value[1..close] : value;
        }

        var colon = value.IndexOf(':');
        if (colon < 0)
        {
            return value;
        }

        return value.IndexOf(':', colon + 1) >= 0 ? value : value[..colon];
    }

    private async Task<bool> CanResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out _))
        {
            // A literal address needs no resolver and cannot be a naming mistake.
            return true;
        }

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                await ResolveAsync(host, cancellationToken);
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (attempt == Attempts)
                {
                    _logger.LogWarning(e, "Datastore host '{Host}' did not resolve after {Attempts} attempts",
                        host, Attempts);
                    return false;
                }

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        return false;
    }
}
