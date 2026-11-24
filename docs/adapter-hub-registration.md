---
description: Adapter hub registration as the only reachable signal: readiness, recovery service, registration retry.
applies_to: src/Sdk.Adapters/{AdapterHub*,AdapterExecutionService,HostedAdapterExecutionService,AdapterBuilder}.cs
---

# Adapter hub registration is the only "reachable" signal

> **Two branches, one policy.** `AdapterHubRecoveryService` and the `ready`-tagged
> `AdapterHubReadinessHealthCheck` are wired in the `else` (dedicated) branch of the pool-member switch
> in `AdapterBuilder`/`WebAdapterBuilder`; a pool member gets their pool-hub counterparts instead (see
> "Pool member recovery" below). 🔴 Never register `AdapterHubRegistration` on a member — it has no
> adapter-hub connection, so the check would keep every member un-ready.

A live SignalR connection is **not** the same as a usable adapter. Registration at the controller's
`adapterHub` is a hub invoke of its own, and it can fail while the connection is up. When it does,
the adapter is *deaf*: RabbitMQ data events and in-process triggers keep running, so pipelines it
already knows about still execute, but everything the controller has to **push** — configuration
updates, `DeployDataFlow`, HTTP-activator calls — goes nowhere. In production five of seven tenant
adapters sat like that for eleven hours behind `1/1 Running`, `/healthz/live` 200, `/healthz/ready`
200 and an adapter entity reading `DEPLOYED / RUNNING`. The only thing that said so out loud was a
deploy answering `has no live SignalR connection`.

`IAdapterHubRegistrationState` (`src/Sdk.Adapters/AdapterHubRegistrationState.cs`) is that missing
signal — a singleton written by `AdapterExecutionService` on every (re)registration and read by:

- **`AdapterHubReadinessHealthCheck`** — tagged `ready`, so it governs `/healthz/ready`. Healthy only
  while `IsRegistered && IAdapterHubClient.IsAlive`. Two softeners keep it off a hair trigger:
  `Adapter:HubReadinessGracePeriod` (5 min) keeps a pod that has **never** registered ready, so a
  controller that is still rolling out or a tenant that is not enabled yet never costs readiness; and
  `Adapter:HubReadinessProbeEnabled` turns the gating off entirely. The adapter chart pairs it with a
  generous `failureThreshold: 12` (2 min).
- **`AdapterHubRecoveryService`** — stops the host after `Adapter:HubRegistrationRecoveryTimeout`
  (15 min) without a registration, so the container restarts and re-registers. In production a restart
  repaired every deaf adapter with no configuration change; this automates exactly that.

🔴 **The readiness probe cannot do the restart.** Kubernetes restarts a container for **liveness**
only — readiness just removes it from the Service endpoints and shows `0/1`. And liveness must stay
independent of the hub (`/healthz/live` deliberately evaluates no check at all): gating it on the
controller would turn one controller outage into a fleet-wide restart storm. Hence the separate
recovery service rather than a re-pointed liveness probe.

🔴 **`AdapterHubRecoveryService` only arms after the first successful registration in the process**
(`HasEverRegistered`). An adapter that never registered may be waiting for a tenant that is not
enabled or a controller that was never deployed — restarting that in a loop repairs nothing and hides
the cause. That guard is what makes the automatic restart safe to default to on.

🔴 **`AdapterHubRecoveryService` stops the host by decision only, never by a failing check**.
An exception that escapes a `BackgroundService` stops the host too, so the service that
exists to restart an adapter after 15 minutes would restart it on the tick a check throws. Its timer
is independent of everything else, which means it also samples the hub client while a tenant update
(`PreUpdateTenantAsync`: stop, wait 5 s, start) has it stopped. `ISignalRClient.IsAlive` used to throw
`ObjectDisposedException` for that whole window, and roughly every sixth cache clear, CK model import
or blueprint install ended the adapter process. Two things hold now: `IsAlive` is a state query that
returns `false` for a stopped client (**octo-sdk**), and a connection state that cannot be read
counts as "not alive" — logged at Error, the outage clock runs, and the adapter restarts only after
the timeout. It is counted rather than skipped on purpose: skipping would let a read that fails for
good hide an unreachable adapter forever. The outage clock also keeps running across a deliberate
stop — a restart that never comes back from `PreUpdateTenantAsync` is exactly what the timeout is
for.

## Registration retry on the (re)connect path

`AdapterExecutionService.RegisterAtHubAsync` retries the register invoke `RegistrationMaxAttempts`
(3) times with a linear back-off (`RegistrationRetryBaseDelay`, 2 s × attempt), then rethrows so the
SignalR (re)connect loop keeps its own retry (swallowing it made the loop treat a failed
registration as success and exit). The observed failure is
`InvokeCoreAsync cannot be called if the connection is not active`: the register invoke races the
connection state right after `HubConnection.StartAsync` returned. On a fresh start the start loop's
next iteration papered over it; on a reconnect after a long outage that single throw was enough.

⚠️ **The pre-invoke wait can only use `IsAlive`, which is `State != Disconnected`** and therefore also
true while the connection is still *Connecting* — exactly the window that produces the exception. A
strict `IsConnected` would have to be added to `ISignalRClient` in **octo-sdk** and shipped as a
NuGet, so the actual wait for an active connection is the retry: the next attempt runs after the
back-off, by which time the connection has either finished connecting or is gone for good.

Failed attempts log at **Error**, not Warn, on purpose: the adapter log carries WARN-level
`[Audit:DataPermissions.ReadViolation]` noise by the dozen per pipeline run and rotates the startup
log away within the hour, which is why this was missed twice.

Tests: `Sdk.Common.Tests/Adapters/AdapterExecutionServiceTests` (a reconnect whose first registration
throws ends registered; all attempts failing marks not-registered and rethrows) and
`Sdk.Common.Tests/Adapters/AdapterHubRegistrationReadinessTests` (readiness matrix, recovery timing,
never-registered guard, and a connection state that cannot be read: it counts as an outage, the
service keeps sampling and restarts only after the timeout).

## Registered, connected — and still deaf

A registration can succeed and still be lost on the controller: a tenant pre/post-update racing it
left the adapter in an orphaned cache instance (fixed controller-side). The adapter cannot detect
that on its own — `ReportAdapterMetricsAsync` is a `SendAsync`, so a server-side rejection never
arrives. The controller therefore detects it from the metrics samples and sends
`PreUpdateTenantAsync` to that connection — the existing "restart and register again" callback, so
nothing changes in this repo's contract. The one change here: `PreUpdateTenantAsync` marks the
registration state **not registered immediately**, not at the end of `StopAsync` (the shutdown may
take up to `AdapterShutdownTimeout`), so readiness and the watchdog never report "registered" for a
process the controller has already written off. Test:
`AdapterExecutionServiceTests.PreUpdateTenantAsync_MarksNotRegisteredBeforeTheShutdownCompletes`.
