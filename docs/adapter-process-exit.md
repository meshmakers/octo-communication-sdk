---
description: "Exit code of an adapter process: a failed build or start, or a failed background service, ends the process with 1, a normal stop with 0."
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
  `BackgroundService` whose `ExecuteAsync` faulted or cancelled itself (below). The guard
  logs one ERROR line `Adapter stopped because of an exception: <type>: <message>`; the builder then
  runs `LogManager.Shutdown()` and ends the process with `Environment.Exit(1)` (`TerminateProcess`,
  internal so the tests can capture it instead of exiting).

🔴 `Environment.Exit` is not optional: a host that fails in `StartAsync` leaves foreground threads
behind (seen with `Sdk.Plug.Simulation` and an unresolvable `Adapter:BrokerHost`: the refusal was
logged, then the process hung). Returning with `Environment.ExitCode = 1` would keep it alive.

Before, the builders caught every exception, logged it and returned, so a startup refusal ended with
exit code 0 (or hung) and Kubernetes showed only a BackOff. The deliberate startup refusals (a datastore
host that does not resolve, an `AdapterPool` section that does not bind) are such failures.
Microsoft.Extensions.Hosting adds its own `Hosting failed to start` ERROR line before the guard's.

## Background services

A faulted `BackgroundService` does not make the host throw: under `BackgroundServiceExceptionBehavior.StopHost`
the host logs it, stops as if asked to and `Run` returns normally. One that throws an
`OperationCanceledException` of its own (an `HttpClient` timeout) ends with a cancelled `ExecuteTask`,
exactly like one stopped by the shutdown. The builders therefore run the host through
`BackgroundServiceWatch` (`src/Sdk.Adapters/BackgroundServiceWatch.cs`):

- Once the host has started, it attaches a continuation to every background service's `ExecuteTask`. An
  `ExecuteTask` that faults, or is cancelled while `ApplicationStopping` is not yet signalled, ended on
  its own; one cancelled after the stop was requested ended because of the shutdown and is no failure.
- After the run, or when the run ends with an `OperationCanceledException` (a start cancelled by a stop
  request), the watch raises an `InvalidOperationException` for the first faulted service, else for the
  first service that cancelled itself. Its message names the service type and the original exception's
  type and message, and the guard logs it as its one ERROR line and ends with 1. A fault that is or wraps
  an `OperationCanceledException` is a fault: the guard never reads the raised exception as a shutdown.
- 🔴 The watch, not the host, stops the host on such an end. If the host stopped first, the stop request
  would already be signalled when the watch's continuation runs, and a service that cancelled itself
  would read as stopped by the shutdown. `TakeOverHostStopOnFailure` therefore sets
  `HostOptions.BackgroundServiceExceptionBehavior` to `Ignore` in a `PostConfigure` and remembers the value
  the adapter configured: with `StopHost` (the default) the watch calls `StopApplication` itself. The
  watch starts watching when the host has started, so a fault while another hosted service is still
  starting stops the host only after the start completes (before, Hosting cancelled the start); the
  process still ends with 1. An adapter that configured `Ignore` keeps running after a fault, as it
  asked for, and ends with 1 when it is stopped later.

Microsoft.Extensions.Hosting adds its own `BackgroundService failed` ERROR line before the guard's.
Not distinguished: a service that cancels itself while a SIGTERM arrives during the start reads as
stopped by the shutdown, and a service that cancels itself before the start was cancelled by a stop
request is not seen at all (only a fault is).

## Tests

`tests/Sdk.Common.Tests/Adapters/AdapterHostRunGuardTests.cs`, `BackgroundServiceWatchTests.cs` and
`AdapterBuilderExitCodeTests.cs`. The builder tests run in a non-parallel collection, because the builder
shuts down the global NLog configuration. A test that makes a real hosted service fail in `StartAsync`
leaves those foreground threads in the test process, and xUnit then reports `Foreground threads were left
running`. The NLog-reload test therefore registers a hosted-service factory that throws
(`AddSingleton<IHostedService>(_ => throw ...)`); it fails when the builder resolves the hosted services
for the watch, before any of them runs, so Hosting's own line does not appear there. The
background-service cases run on a test host in `BackgroundServiceWatchTests.cs`, because
`Sdk.Plug.Simulation` has no switch that makes a background service fail. The start path of a real hosted
service (`DatastoreHostPreflight.StartAsync`) was checked on the real `Sdk.Plug.Simulation` process.
