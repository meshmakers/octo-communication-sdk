---
description: Tenant scope of an execution: IAdapterTenantScope, AdapterOptions.DedicatedTenantId and the orchestrator requirement.
applies_to: src/Sdk.Pipeline/Services/{*TenantScope*,IAdapterLeaseScope}.cs, src/Sdk.Adapters/Configure*AdapterTenantId.cs, tests/**/TenantIsolation/**
---

# Tenant isolation — `AdapterOptions.TenantId` is gone

Shared adapter leasing (`octo-communication-controller-services/docs/concepts/shared-adapter-leasing.md`)
will lease one adapter process to a tenant for one work item at a time. The invariant that makes
that safe is that the process retains **nothing tenant-scoped** between work items, and the largest
single threat to it was not a subtle cache — it was a process-wide tenant value that looks like the
right answer.

🔴 **`AdapterOptions.TenantId` was therefore DELETED, not deprecated.** While it existed, any node or
service could read it instead of the tenant of the work item it was actually running; on a leased
process that is another tenant's data with a log line that looks entirely plausible. Deleting it
turns every such read into a compile error — which is the only mechanism that actually works.

| Use | Read this |
|---|---|
| Connection-level: the adapter's own hub route, its own credential, its own unregister-on-shutdown | `AdapterOptions.DedicatedTenantId` |
| Inside a node | `IEtlContext.TenantId` / `INodeContext` — unchanged, the node layer was always explicit |
| Services around the node layer (HTTP routing, token acquisition, caches) | `IAdapterTenantScope` |

**`IAdapterTenantScope`** (`src/Sdk.Pipeline/Services/`) is the tenant of the **current execution**.
`EtlDataOrchestrator.ExecutePipelineAsync` enters it from `etlContext.TenantId` — one chokepoint,
which every execution passes through. On a dedicated adapter the value equals the adapter's own
tenant on every execution; leasing changes only the value, never this site.

- A **singleton carrying an `AsyncLocal`**, not a scoped service: executions run concurrently and a DI scope does not flow across an async call chain, whereas the tenant of an execution must. The `AsyncLocal` is an **instance** field — a static one would couple two hosts sharing a process.
- `TenantId` **throws** outside an execution rather than returning null or a default. A caller reaching for the tenant outside one has a bug, and the outcome that must never happen is a plausible-looking wrong answer.
- Nested `BeginExecution` **restores** the outer tenant on dispose, so a node starting a sub-pipeline does not strip the tenant off the rest of the outer pipeline.
- `IsPoolMember` is hard-coded `false` and deliberately **not** bindable — an environment variable that could flip it would defeat the guarantee that a process built as a dedicated adapter never becomes a pool member.

⚠️ **`OCTO_ADAPTER__TENANTID` is still bound for one release** by `ConfigureLegacyAdapterTenantId`,
with a deprecation warning naming the adapter. Eight Helm charts set that key, and the options binder
ignores a key with no matching property **silently** — without the shim every one of those adapters
would have fallen back to the `"meshTest"` default and connected on the wrong tenant route, reporting
nothing at startup. The new key is `OCTO_ADAPTER__DEDICATEDTENANTID` and wins when both are set.

**Tests** (`tests/Sdk.Common.Tests/TenantIsolation/`): the reflection guard that
`AdapterOptions` carries no member named `TenantId` by any route; a DI sweep asserting every SDK
singleton whose surface mentions a tenant is on a cleared allow-list **with a reason**; concurrent
interleaving over several tenants; a poison canary; randomised interleavings.

🔴 **What these tests are not.** They exercise the *scope mechanism* under concurrency, not a real
two-tenant pipeline execution. ✅ The full interleave suite lives in
`octo-mesh-adapter` (`tests/MeshAdapter.Sdk.IntegrationTests/Leasing/LeasedTenantIsolationTests`):
pipeline output over two real tenant databases, the poison canary, the post-release state, the
log-target and identity assertions, plus the mesh-side DI sweep where the caches actually live.

## 🔴 `EtlDataOrchestrator` now *requires* `IAdapterTenantScope`

The orchestrator resolves it with `GetRequiredService`, with no tolerance for a host that registered no
scope: on a pool member a missing scope would mean every execution silently runs with no tenant entered.

Removing it is only safe because **the registration moved into `AddDataPipeline()`**, the same call
that registers the orchestrator — `TryAddSingleton`, so a pool member that registered the lease-aware
scope first is not overwritten. Before the move, `IAdapterTenantScope` was registered only in
`AdapterBuilder` / `WebAdapterBuilder`, and `GetRequiredService` would have broken
`octo-adapter-sap`'s `Program.cs`, `octo-plug-zenon`'s `AdapterInstanceEntryPoint`, both samples here,
and roughly a dozen test fixtures across this repo, `octo-mesh-adapter` and `octo-adapter-weclapp`.
