---
description: "Adapter pool member: lease scope, lease client ordering, participants and the node descriptors it registers."
applies_to: src/Sdk.Adapters/AdapterPool*.cs, src/Sdk.Adapters/IAdapterLease*.cs, src/Sdk.Adapters/AdapterNodeDescriptorProjection.cs
---

# Adapter pool membership

`AddAdapterPoolMember()` (`src/Sdk.Adapters/AdapterPoolServiceCollectionExtensions.cs`) turns an
adapter host into a **pool member**: a process that belongs to no tenant and is handed one per lease.

🔴 **Call it before `AddDataPipeline()`.** The pipeline registration uses `TryAddSingleton` for
`IAdapterTenantScope`, so whichever runs first wins — and on a pool member the winner must be
`AdapterPoolTenantScope`. Registering the dedicated scope in a pool member fails nowhere: it simply
never enforces the lease, and every execution looks fine.

🔴 **Being a pool member is a property of the image, not of a flag.** It is an explicit call in a
composition root rather than a bindable switch, for the same reason `AdapterTenantScope.IsPoolMember`
is hard-coded false: a process that was not built as a pool member must not become one by environment
variable.

## 🔴 A pool member carries no `DedicatedTenantId` — enforced, not assumed

`ConfigurePoolMemberAdapterTenantId` (`IPostConfigureOptions<AdapterOptions>`, registered by
`AddAdapterPoolMember()`) **clears** `AdapterOptions.DedicatedTenantId` whenever the pool
configuration is complete, and logs one warning if something had configured it.

It exists because "it is null on a pool member" was a claim three call sites in two repositories rely
on and that nothing made true: `AdapterOptions`' constructor sets `"meshTest"`, and the local
runbook instructed the operator to set the key to the **lending** tenant. The reads are
`AdapterExecutionService.CkModelChangedAsync`, `HttpRequestService` (route prefix and authorization)
and — the one that costs something — `ServiceAccountTokenService.ResolveTenantId` in
`octo-mesh-adapter`, where a `ServiceAccountConfiguration` naming no tenant falls back to this value.
With the lender's id present, a borrower's leased execution would have acquired a token for the
lender instead of declining.

The member's own connection credential is unaffected: `ConfigureAdapterAuthenticatorOptions` derives
it from `AdapterPoolMemberOptions.PoolTenantId`. Nothing else on a member reads the property —
`AdapterHubClient` and `AdapterExecutionService` are not registered at all in the pool-member branch
of either builder. The clearing is gated on `AdapterPoolMemberOptions.IsEnabled` rather than on the
call, so a host that composes the member services while the configuration names no pool keeps its
dedicated tenant.

Tests: `tests/Sdk.Common.Tests/Adapters/ConfigurePoolMemberAdapterTenantIdTests.cs`.

## `AdapterPoolTenantScope` — two nested notions of "the current tenant"

A **lease** binds the whole process to one borrowing tenant for one work item; an **execution** is one
pipeline run inside it. On a dedicated adapter only the execution notion exists, which is why
`IAdapterTenantScope` stays the interface the fleet consumes and `IAdapterLeaseScope` only adds the
lease half.

- The **lease tenant is a process-wide field, not an `AsyncLocal`** — and it has to be. The lease
  arrives on a hub callback and the executions it serves run on entirely different async call chains,
  so an `AsyncLocal` set by the callback would never reach them. That is exactly the process-wide
  tenant value the concept warns about, and it is safe for **one** reason: it is null between leases.
- A second `BeginLease` while one is held **throws**. Two overlapping leases on one process is the
  cross-tenant incident the design exists to make impossible.
- `BeginExecution` for a tenant other than the leased one **throws**. Nothing in the SDK is supposed
  to do that — which is precisely the assumption that produces a cross-tenant read when a trigger
  fires late or a queued item is picked up just after its lease ended.

## `AdapterPoolClient` — the ordering is the invariant

Enter the lease scope → enter every `IAdapterLeaseParticipant` in registration order → run the
`IAdapterLeaseWorkItem` → leave the entered participants in **reverse** order → leave the lease scope
→ **only then** report the release. Reporting first would open exactly the window the design exists to
close, because the controller's next act is to hand the member another tenant.

- A participant whose **enter** threw is never left: it has nothing to leave, and tearing down a
  half-constructed state in a way nobody designed is how a tenant survives a release.
- A participant whose **leave** throws puts the member into **draining**. Concept §6: a member whose
  post-lease cleanliness is unproven is drained and restarted, never re-used. That is a state change,
  not a logged shrug.
- A second concurrent lease is **refused**, not queued. The controller's registry makes it impossible,
  so reaching there means the two views diverged — and the safe answer to "I may already be serving
  somebody else" is never "serve them both".
- Both directions degrade through the once-only `HubException` pattern (same as the scale-status
  channel): controller, `octo-sdk` and this SDK ship together, so it only covers a rolling upgrade.

## Registration carries the member's node descriptors

`AdapterPoolClient.RegisterAsync` sends `NodeDescriptors` + `PipelineSchemaJson` on
`PoolMemberRegistrationDto` — the **same** values a dedicated adapter sends on
`RegisterAdapterWithSchemaAsync`, projected by the shared `AdapterNodeDescriptorProjection`
(extracted out of `AdapterExecutionService`; there is deliberately only one projection, because two
would be two answers to one question).

🔴 **Why it matters on the other side of the wire:** the controller has no other source of "which
nodes can this pool run". A *borrowing* tenant's `DeployPipeline` resolves the pipeline's execution
class and validates its definition against these descriptors, so a member that reports none leaves
every leased pipeline at the CK default `Batch` and validated against no schema. The predecessor
field `NodeNames` was hard-coded to `[]` here and read nowhere, which is why the gap was invisible.

`INodeSchemaRegistry` and `IPipelineSchemaGenerator` are **optional** constructor parameters: a host
that composed no data pipeline still registers and stays leasable, and a registry that throws is
logged and degraded, never propagated.

## `IAdapterLeaseParticipant` — the isolation invariant, made composable

The SDK cannot see the caches an adapter repository owns, so each of them registers a participant
instead of the SDK carrying a hard-coded list it would have to keep in step. "Did this member really
drop everything" becomes a question a test can answer by enumerating participants.
`octo-mesh-adapter` registers three: the borrower identity, the CK model cache and the pipeline
registry.

⚠️ `IAdapterLeaseWorkItem` defaults to `NoAdapterLeaseWorkItem` — an honest "nothing queued for me",
so a host that runs leased work registers its own implementation. The member still runs the full
enter/leave/release cycle either way, which keeps the wire contract verifiable.

Tests: `tests/Sdk.Common.Tests/TenantIsolation/AdapterPoolTenantScopeTests.cs`,
`tests/Sdk.Common.Tests/Adapters/AdapterPoolClientTests.cs`,
`tests/Sdk.Common.Tests/TenantIsolation/OrchestratorRequiresTheTenantScopeTests.cs`, and the
pool-member arm of `SingletonTenantFreedomSweepTests`.
