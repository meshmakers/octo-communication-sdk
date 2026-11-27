# Changelog

Notable changes of the OctoMesh communication SDK. Versions are set by the release pipeline; entries
collect under `Unreleased` until the next communication release train.

## [Unreleased]

### Added

- `Logger@1` logs values from the data context (AB#9335, Feature AB#9249). New optional properties:
  `messagePath` (JSONPath to the message; exactly one of `message` and `messagePath` is required),
  `level` (`Debug`, `Information`, `Warning`, `Error`; default `Information`), `attributes` (list of
  `name` plus `value` or `valuePath`, at most 20, emitted as structured log properties) and
  `missingValue` (text for a missing or null value, default `<missing>`). Missing data never fails
  the node; objects and arrays are logged as compact JSON; the message is limited to 2048 and every
  attribute value to 256 characters, ending in `...[truncated]` when cut; values registered as secret
  are masked. Every Logger@1 line also carries the attribute `NodePath`, and `TenantId` and
  `PipelineRtId` when the execution's `IEtlContext` can be resolved (always in the mesh adapter and
  the SDK's adapter host). The attributes reach the log output through the SDK's pipeline loggers; a
  custom `IPipelineLogger` that does not override `Log` drops them. An invalid configuration fails
  the execution with a `PipelineConfigurationException`, which the orchestrator reports wrapped in a
  `DataPipelineException`. The node schema publishes the new properties.
- `IPipelineLogger.Log(LogLevel, nodeId, nodePath, message, attributes)` and
  `INodeContext.Log(LogLevel, message, attributes)`: log a message as a value with structured
  attributes. Both are default interface members, so existing implementations keep compiling.

### Changed

- `Logger@1`: `message` is no longer required (see `messagePath`). Existing definitions keep the body
  format `<nodePath>: <message>` and level Information. The new limits apply to them too: a static
  message longer than 2048 characters is now cut, and values registered as secret are masked. The
  message is now passed as a value instead of a format template; braces such as `{0}` were already
  kept verbatim by NLog when no arguments were given, and now no longer depend on that.

### Fixed

- An adapter whose host fails to build or start now ends its process with exit code 1 instead of 0
  (AB#9682), so Kubernetes shows a crash instead of a clean exit. `AdapterBuilder.Run` and
  `WebAdapterBuilder.RunAsync` log one ERROR line naming the exception type and message
  (`Adapter stopped because of an exception: <type>: <message>`), flush NLog and end the process
  with `Environment.Exit(1)`; signatures are unchanged. `Environment.Exit` is needed because a host
  that fails in `StartAsync` leaves foreground threads behind: before, such a process logged the
  refusal and then hung instead of exiting. A normal stop (SIGTERM, drain exit, `StopApplication`)
  and a cancellation after a shutdown request while starting still return normally and end with 0.
  The deliberate startup refusals (a datastore host that does not resolve, an `AdapterPool` section
  that does not bind) are such failures.
- An adapter whose `BackgroundService` faults, or cancels itself before a shutdown was requested (an
  `HttpClient` timeout in `ExecuteAsync`), now ends its process with exit code 1 instead of 0
  (AB#9744), also when it happens while a stop request cancels the start. The guard's one ERROR line
  names the service type and the original exception type and message. A normal stop still ends with 0.
  Once the host has started, the builders switch `HostOptions.BackgroundServiceExceptionBehavior` from
  `StopHost` (the default) to `Ignore` and stop the host themselves; while it starts, `StopHost` stays in
  force. They do not take over when a hosted service is registered as transient. An adapter that
  configured `Ignore` keeps running after a fault and ends with 1 when it is stopped.
