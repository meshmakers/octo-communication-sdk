---
description: Trigger status line, start-of-execution report and failure kind of a failed execution.
applies_to: src/Sdk.Pipeline/EtlDataPipeline/AdapterTriggerContext.cs, src/Sdk.Pipeline/Services/*Reporter*.cs, src/Sdk.Pipeline/Services/PipelineExecution*.cs
---

# The trigger → pipeline status line

`ITriggerContext.ReportStatusAsync(message, isError, ct)` is how a trigger node tells the operator
what its last poll did. The line travels `AdapterTriggerContext` → `IPipelineExecutionReporter.
ReportPipelineStatusAsync` → `IAdapterHubClient.ReportPipelineStatusAsync(PipelineStatusReportDto)`
(octo-sdk, fire-and-forget `SendAsync`) → the controller's `AdapterHub`, which writes **only** the
pipeline entity's `StatusMessage`. It exists because the one adapter → controller channel that
wrote `StatusMessage` before, `SendDeploymentUpdateResultAsync`, is adapter-wide and sets the
`DeploymentState` of every pipeline — a poll outcome cannot go through it.

Three things are load-bearing:

- **Fire-and-forget for the caller.** `ReportStatusAsync` never throws and never blocks a poll on
  the controller; the reporter swallows every delivery failure and logs it at **Debug**, rate-limited
  to one line per `AdapterPipelineExecutionReporter.StatusFailureLogInterval` (5 min) with the count
  of swallowed failures folded into the next line. A poll loop reports every interval, so a
  per-failure warning against an old or unreachable controller would be a log flood.
- **A controller predating the method drops the line on ITS side.** The SDK client sends with
  `SendAsync`, so no error reaches the adapter at all — the pipeline's `StatusMessage` simply stays
  what it was. What the adapter does see is a connection that is not active
  (`InvalidOperationException`), hence the rate limit.
- **`TriggerContext.ReportStatusAsync` is a virtual no-op, not abstract**: the base is subclassed
  outside this repository (`octo-mesh-adapter`'s `MeshAdapterTriggerContext` is a hand-maintained
  copy of `AdapterTriggerContext`), and a line nobody delivers is harmless, whereas an abstract
  member would break such a subclass on the package update. Hosts with a reporter override it.

Senders keep the line short (the controller truncates at 1000 characters) and never put credentials
or message bodies in it. Tests: `AdapterPipelineExecutionReporterTests` (DTO, never-throw, rate
limit) and `AdapterTriggerContextTests` (forwarding, no-reporter no-op).

## The start is reported before the context exists

`AdapterTriggerContext.StartExecutePipelineAsync` generates the execution id, captures the start
time and calls `IPipelineExecutionReporter.ReportExecutionStartAsync` **before**
`IContextCreatorService.CreateEtlContext`. Context creation (and the debugger setup behind it) is
wrapped in a try/catch that logs at Error with the tenant id, reports
`ReportExecutionEndAsync(Failed, ex.Message)` under the same id and rethrows. It used to be the
other way round, and a context creation that throws (a production adapter: the mesh adapter's
`FindTenantRepositoryAsync` failing with "System tenant database does not exist" on every cron
tick for four days) never reached the reporter — no execution on the controller, frozen
`PipelineStatistics`, `octo.pipeline.execution.failures` at zero, no alert. No double report:
`RegisterExecution` runs only after the try, so `EndExecutePipelineAsync` is never called for a
failed id, and a pipeline that fails *after* the start still ends exactly once through the
existing end path. The mesh adapter's hand-maintained copy carries the same order — keep them in
step. Tests: `AdapterTriggerContextTests` (`StartExecutePipelineAsync_WhenContextCreationFails_*`,
`StartExecutePipelineAsync_ReportsTheStartBeforeTheContextIsCreated`).

## Failure kind of a failed execution

A failed execution reports **what it means for its writes** next to `PipelineExecutionStatus.Failed`:
the new seven-parameter overload `IPipelineExecutionReporter.ReportExecutionEndAsync(..., outputData, failureKind)` fills
`PipelineExecutionEndDto.FailureKind` (a default interface member that forwards to the unchanged six-parameter member, so
existing implementers keep compiling and binding but drop the kind until they override it), and a pool member's release fills
`LeaseResultDto.FailureKind` from `LeaseWorkOutcome.FailureKind` (an `init` property, not positional, so constructor and
`Deconstruct` keep their shape; new overload `LeaseWorkOutcome.Failed(message, failureKind)` next to `Failed(message)`). The value comes from
`PipelineExecutionFailureKinds.Of(exception)` (octo-sdk), i.e. the engine's `UnacknowledgedWriteClassifier`:
`OutcomeUnknown` when a MongoDB write was not acknowledged (write-concern error, `UnknownTransactionCommitResult`) and may
have taken effect, `Failed` otherwise. Set at the same three places as in the mesh adapter: `AdapterTriggerContext.EndExecutePipelineAsync`
(pipeline task faulted), `ReportExecutionFailedBeforeRunAsync` (context creation failed) and `AdapterPoolClient` (a work item that
threw; a work item may also return its own kind). The reporter sends a kind only with `Failed`; the interrupted-execution report
(`ReportInterruptedExecutionResultAsync`) carries none (no exception is known there). The communication controller maps
`OutcomeUnknown` of a pipeline method to `METHOD_OUTCOME_UNKNOWN` + `correlationId`. Never put a marker into the error
message instead. Tests: `AdapterTriggerContextTests` (`*NotAcknowledged*`, `*UnacknowledgedWrite*`), `AdapterPoolClientTests`
(`*UnacknowledgedWrite*`, `*OutcomeUnknown*`); `FakeWriteConcernFaultException` stands in for the driver exception.
