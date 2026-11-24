---
description: "Exit code of an adapter process: a failed build or start ends the process with 1, a normal stop with 0."
applies_to: src/Sdk.Adapters/{AdapterBuilder,AdapterHostRunGuard,BackgroundServiceWatch}.cs, src/Sdk.Common.Web/Sockets/WebAdapterBuilder.cs
---

# Adapter process exit code

`AdapterBuilder.Run` and `WebAdapterBuilder.RunAsync` run the host through `AdapterHostRunGuard`
(`src/Sdk.Adapters/AdapterHostRunGuard.cs`), which decides the process exit code:

- **0** when the host returns (SIGTERM, drain exit, `StopApplication`) or when an
  `OperationCanceledException` arrives after `ApplicationStopping` was signalled (a shutdown requested
  while the host was starting). A stop in which a hosted service's `StopAsync` throws, or the
  `HostOptions.ShutdownTimeout` expires, reaches the guard as an `AggregateException` and ends with 1
  and an ERROR line, although the pod is being stopped anyway.
- **1** (`AdapterHostRunGuard.FailureExitCode`) for every other exception while building, starting or
  running the host, including a cancellation nobody asked for (an `HttpClient` timeout), and for a
  `BackgroundService` whose `ExecuteAsync` faulted or was cancelled before a shutdown was requested. The guard
  logs one ERROR line `Adapter stopped because of an exception: <type>: <message>`; the builder then
  runs `LogManager.Shutdown()` and ends the process with `Environment.Exit(1)` (`TerminateProcess`,
  internal so the tests can capture it instead of exiting).

🔴 A faulted background service does not make the host throw: under the default
`BackgroundServiceExceptionBehavior.StopHost` the host logs it, stops as if asked to and `Run` returns
normally; one that ends with an `OperationCanceledException` of its own does the same and its
`ExecuteTask` reads as cancelled, not faulted. The builders therefore create a `BackgroundServiceWatch`
(`src/Sdk.Adapters/BackgroundServiceWatch.cs`) over the hosted services before the run. It notes, when
`ApplicationStopping` fires, any background service whose `ExecuteTask` is already cancelled, and
`ThrowIfAnyFailed()` after the run raises the first faulted `ExecuteTask`'s exception, or an
`InvalidOperationException` naming the service that was cancelled before the shutdown. A service
cancelled by the shutdown itself is no failure.

🔴 `Environment.Exit` is not optional: a host that fails in `StartAsync` leaves foreground threads
behind (seen with `Sdk.Plug.Simulation` and an unresolvable `Adapter:BrokerHost`: the refusal was
logged, then the process hung). Returning with `Environment.ExitCode = 1` would keep it alive.

Before, the builders caught every exception, logged it and returned, so a startup refusal ended with
exit code 0 (or hung) and Kubernetes showed only a BackOff. The deliberate startup refusals (a datastore
host that does not resolve, an `AdapterPool` section that does not bind) are such failures.
Microsoft.Extensions.Hosting adds its own `Hosting failed to start` ERROR line before the guard's.
Only hosted services registered as singletons (`AddHostedService` does that) are watched: the builder
resolves them once more before the run, and a transient registration would hand it another instance. Under `BackgroundServiceExceptionBehavior.Ignore` a fault that happened earlier
turns a later SIGTERM into exit 1.

## Tests

`tests/Sdk.Common.Tests/Adapters/AdapterHostRunGuardTests.cs`, `BackgroundServiceWatchTests.cs` and
`AdapterBuilderExitCodeTests.cs`. The builder tests run in a non-parallel collection, because the builder shuts down the global NLog
configuration. A test that makes a real hosted service fail in `StartAsync` leaves those foreground
threads in the test process, and xUnit then reports `Foreground threads were left running`. The
NLog-reload test therefore registers a hosted-service factory that throws
(`AddSingleton<IHostedService>(_ => throw ...)`); it fails when the builder resolves the hosted services
before the run, not in `Host.StartAsync`, so Hosting's own line does not appear there. The start path
(`DatastoreHostPreflight.StartAsync`) was checked on the real `Sdk.Plug.Simulation` process.
