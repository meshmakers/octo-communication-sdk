---
description: "Pool member recovery: readiness, self-heal, lease resumption, draining, two token holders."
applies_to: src/Sdk.Adapters/{AdapterPool*,HubRegistration*}.cs, **/PipelineRegistryService.cs, tests/**/AdapterPool*
---

# Pool member recovery

The pool member's counterpart of `docs/adapter-hub-registration.md`. Same policy, same configuration keys
(`Adapter:HubReadinessProbeEnabled`, `HubReadinessGracePeriod`, `HubRegistrationRecoveryEnabled`,
`HubRegistrationRecoveryTimeout`), different hub. The decision logic lives once in the abstract
`HubRegistrationRecoveryService` / `HubRegistrationReadinessHealthCheck`; the dedicated and the member
classes differ only in which connection's `IsAlive` they sample.

- **Deaf member** = the controller holds no registration for this member's *current* connection: the
  pool hub is down, the registration was refused (`Accepted=false` from a controller pod that is
  shutting down, or a `HubException` from an enforcing tenant binding), or the controller rejected a
  heartbeat (it does so for a connection it has no registration for). A deaf member is quieter than a
  deaf dedicated adapter: it has no pipelines of its own between leases, so it simply never gets work.
- **State**: `AdapterPoolClient.RegistrationState` (the shared `IAdapterHubRegistrationState`
  singleton, `TryAdd` in `AddAdapterPoolMember()` *and* the builders' member branch — both sides must
  see one instance). Written on every registration attempt and on a rejected heartbeat.
- **Readiness**: `AdapterPoolHubReadinessHealthCheck`, check name `AdapterPoolHubRegistration`, tag
  `ready` (WebAdapterBuilder only). 🔴 It follows the registration, **never a lease** — an idle member
  is exactly as ready as a busy one.
- **Recovery**: `AdapterPoolHubRecoveryService` stops the host after the timeout, armed only after the
  first registration in the process. A lease still running then is lost with the process; the
  controller interrupted and re-queued it when the connection went away (concept §6).
- **Self-heal without restart** (`AdapterPoolMemberService.TickAsync`): connection down → nothing (the
  SignalR client owns reconnecting, its callback registers); registered → heartbeat; up but not
  registered → register again instead of heartbeating into nothing.
- 🔴 **Reconnect during a lease registers WITH the lease** (`AdapterPoolClient.ResumeAsync` →
  `ResumePoolMemberAsync`): the controller takes the lease over — after a controller restart it would
  otherwise not know it, after a dropped connection it holds the work back for a reconnect grace instead
  of re-queuing it — and the member's release completes the execution. Only against a controller that
  pre-dates the method (`HubException`/`NotSupportedException`) is the registration **deferred** until
  the lease is released, as before; that answer is latched per connection (`IsResumptionUnsupported`,
  reset by `OnNewConnection`). A plain registration would get the busy member the next lease at once,
  which it must refuse — failing that borrower's execution.
- 🔴 **A release that could not be reported is kept** (`_pendingReleases`) and re-sent on the
  next connect **before** the member registers, and on every tick. It used to be dropped ("the
  controller will expire it on TTL"), which lost the work item's result. The release now names
  `ExecutionId`, `TenantId` and `MemberId`, so a controller that no longer holds the lease can attribute
  it. `IsDrainedAndIdle` waits for pending releases. Resumption and release reports share one gate
  (`_hubReportGate`), and the running lease is read inside it, so a release never overtakes the
  resumption of the same lease. Tests: `AdapterPoolMemberLeaseResumptionTests`.
- **CK model cache flush** after a reconnect, per *recently leased* tenant
  (`AdapterPoolClient.RecentlyLeasedTenantIds` = running lease + last entered), via the host's
  `IAdapterService.CkModelChangedAsync`, **before** registering so no new lease warms a cache about to
  be dropped. `CkModelChanged` never reaches a member at all (no adapter-hub connection); the mesh
  adapter's `CkModelCacheLeaseParticipant` loads/unloads per lease, so between leases there is nothing
  stale — the flush covers the lease running across the reconnect and hosts without such a participant.
- The heartbeat now sends the held lease **id** as `ActiveLeaseId` (it used to send the tenant id).

Tests: `Sdk.Common.Tests/Adapters/AdapterPoolMemberRecoveryTests`.

## A draining member ends as a process

The drain contract always said "finish what you hold, take nothing new, **exit**", and no member ever
exited: on a test cluster a failed leave drained the only member of a pool, which then stayed `1/1 Ready`,
refused every lease the controller kept granting it, and the pool was dead until the pod was deleted.

- **A failed leave keeps the work item's outcome.** It is a fact about the process, not the pipeline:
  the release carries `Reason=Drained` with the work item's own `Success`/`OutputData`, and the status
  message names the participant. Overwriting it with `Failed` turned completed work into a FAILED
  execution the borrower would run again.
- **No re-registration while draining** (tick and reconnect). The controller only knows about the drain
  for the connection it was reported on; a registration on a new connection starts as available.
- **Readiness is red** while draining (`AdapterPoolHubReadinessHealthCheck`, optional `AdapterPoolClient`
  ctor parameter; the probe switch still wins).
- **`AdapterPoolMemberDrainExitService`** (both builders, member branch) stops the host once
  `AdapterPoolClient.IsDrainedAndIdle` — draining, no lease, **and the lease gate free**, i.e. the
  release has been reported. 🔴 Not `CurrentLease == null`: that is cleared before the report, and an
  exit in between would make the controller interrupt and re-run completed work. Off switch:
  `AdapterPool:ExitWhenDrained=false` (`OCTO_ADAPTERPOOL__EXITWHENDRAINED`), default `true`.

Tests: `Sdk.Common.Tests/Adapters/AdapterPoolMemberDrainTests`.

## A pool member never starts trigger nodes

On a member the lease **is** the trigger: the controller turns cron ticks (`LeaseTriggerMessage`) and execute calls into queued work, and the member runs it through `IAdapterLeaseWorkItem`.
`PipelineRegistryService` therefore registers a pipeline **without** calling
`StartTriggerPipelineNodesAsync` when `IAdapterTenantScope.IsPoolMember` is true (resolved lazily,
leniently — a composition without a scope keeps the dedicated behaviour). The registration itself
stays: the work item resolves it to run the pipeline, and `PipelineRegistryLeaseParticipant` drops it
on leave.

Why: a started `FromPipelineTriggerEvent@1` declared and bound the borrower's durable trigger queue
on a bus the member never starts and left it behind on release; its stop then waited for
the never-reached endpoint ready state until `TriggerStopTimeout` (30 s), the leave
failed and the member drained on **every** leased run of the canonical cron form. Tests:
`PipelineRegistryServiceTests.PoolMember_*` / `DedicatedAdapter_RegisterPipelineAsync_StillStartsTriggerNodes`.

## Two identities, two token holders

A pool member presents **its own** identity (the lending pool's tenant, from `Adapter:ClientId` +
`AdapterPool:AdapterPoolTenantId`) on the `/adapterPoolHub` management connection, and the
**borrower's** identity on everything a leased execution does. They live in two holders:
- `AdapterPoolHubAccessToken` — read only by `AdapterPoolHubClient` (registered by factory in
  `AddAdapterPoolMember`), written by `AdapterAccessTokenService` on a member
  (`AddAdapterAccessTokenService(isPoolMember: true)`, shared by both builders).
- the process-wide `IServiceClientAccessToken` — the lease identity: the borrower's token while a
  lease runs (`BorrowerIdentityLeaseParticipant`, octo-mesh-adapter), empty between leases.

Why: SignalR's `AccessTokenProvider` reads the client's holder on every (re)connect. With one shared
holder a pool hub connection rebuilt during a lease (controller restart) was authenticated as the
borrower, and the deferred re-registration went out on it — refused under
`AdapterPoolHubAuthorization:Mode=Enforce`. It also let the own-credential
refresh overwrite a borrower token mid-lease, and a lease leave wipe the member's own token. A
dedicated adapter is unchanged (one identity, process-wide holder). Tests: `AdapterPoolHubIdentityTests`.
