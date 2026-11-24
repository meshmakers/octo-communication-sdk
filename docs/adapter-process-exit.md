---
description: "Exit code of an adapter process: a failed build or start ends the process with 1, a normal stop with 0."
applies_to: src/Sdk.Adapters/{AdapterBuilder,AdapterHostRunGuard}.cs, src/Sdk.Common.Web/Sockets/WebAdapterBuilder.cs
---

# Adapter process exit code (AB#9682)

`AdapterBuilder.Run` and `WebAdapterBuilder.RunAsync` run the host through `AdapterHostRunGuard`
(`src/Sdk.Adapters/AdapterHostRunGuard.cs`), which decides the process exit code:

- **0** when the host returns (SIGTERM, drain exit, `StopApplication`) or when an
  `OperationCanceledException` arrives after `ApplicationStopping` was signalled (a shutdown requested
  while the host was starting).
- **1** (`AdapterHostRunGuard.FailureExitCode`) for every other exception while building, starting or
  running the host, including a cancellation nobody asked for (an `HttpClient` timeout), and for a
  `BackgroundService` whose `ExecuteAsync` faulted. The guard
  logs one ERROR line `Adapter stopped because of an exception: <type>: <message>`; the builder then
  runs `LogManager.Shutdown()` and ends the process with `Environment.Exit(1)` (`TerminateProcess`,
  internal so the tests can capture it instead of exiting).

🔴 A faulted background service does not make the host throw: under the default
`BackgroundServiceExceptionBehavior.StopHost` the host logs it, stops as if asked to and `Run` returns
normally. The builders therefore resolve the hosted services before the run and call
`AdapterHostRunGuard.ThrowIfAnyBackgroundServiceFaulted` after it, which raises the first faulted
`ExecuteTask`'s exception (a cancelled one is no failure).

🔴 `Environment.Exit` is not optional: a host that fails in `StartAsync` leaves foreground threads
behind (seen with `Sdk.Plug.Simulation` and an unresolvable `Adapter:BrokerHost`: the refusal was
logged, then the process hung). Returning with `Environment.ExitCode = 1` would keep it alive.

Before, the builders caught every exception, logged it and returned, so a startup refusal ended with
exit code 0 (or hung) and Kubernetes showed only a BackOff. The deliberate startup refusals (a datastore
host that does not resolve, an `AdapterPool` section that does not bind) are such failures.
Microsoft.Extensions.Hosting adds its own `Hosting failed to start` ERROR line before the guard's.

## Tests

`tests/Sdk.Common.Tests/Adapters/AdapterHostRunGuardTests.cs` and `AdapterBuilderExitCodeTests.cs`. The
builder tests run in a non-parallel collection, because the builder shuts down the global NLog
configuration. A test that makes a real hosted service fail in `StartAsync` leaves those foreground
threads in the test process, and xUnit then reports `Foreground threads were left running`. Fail the
start through a hosted-service factory that throws instead (`AddSingleton<IHostedService>(_ => throw ...)`).
