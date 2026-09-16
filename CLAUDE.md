# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **Important**: Keep this file up to date when the codebase changes — new nodes, new services, or a
> change to one of the contracts written down below.

## Project Overview

**octo-communication-sdk** is the framework every OctoMesh adapter is built on: the ETL pipeline
engine, the node model, the trigger/execution contracts, and the adapter host. It has no adapter of
its own — it is consumed as NuGet packages by `octo-mesh-adapter`, `octo-adapter-loxone`,
`octo-plug-zenon` and the other adapter repos, so **a change here is a change to every adapter**.

It was carved out of `octo-sdk` in Phase 3 of the pipeline-YAML migration. The namespaces still read
`Meshmakers.Octo.Sdk.Common.*` on purpose — keeping them stable meant the nine consumer repos needed
no source changes beyond a package reference.

### Projects

| Project | Package | What it is |
|---|---|---|
| `src/Sdk.Pipeline` | `Meshmakers.Octo.Sdk.Pipeline` | The ETL pipeline + node framework. `EtlDataPipeline/` holds the orchestrator, the path-only `IDataContext`, the node base types and the built-in nodes; `Services/` holds the execution contracts (`ExecutePipelineOptions`, `PipelineRegistration`, `VerifiedPrincipal`, the registry and polling services). |
| `src/Sdk.Adapters` | `Meshmakers.Octo.Sdk.Adapters` | The adapter host: `AdapterBuilder`, startup/shutdown, the controller hub callback, health and metrics. |
| `src/Sdk.CommunicationAdapter` | `Meshmakers.Octo.Sdk.CommunicationAdapter` | The `System.Sdk` CK model and the adapter contracts that go with it. |
| `src/Sdk.Common.Web` | `Meshmakers.Octo.Sdk.Common.Web` | ASP.NET Core hosting for sockets and plugs. |
| `src/Sdk.SimulationNodes` | `Meshmakers.Octo.Sdk.SimulationNodes` | Simulation pipeline nodes and their generators. |
| `src/Sdk.Plug.Simulation` | *(executable)* | The plug-simulation service; publishes its own Helm chart (AB#4948). |

`samples/` holds `Sdk.Plugs.Sample` and `Sdk.Socket.WebSample`; neither is packaged.

## Build and Test Commands

```bash
# Local development — 999.0.0, restores from ../nuget
dotnet build -c DebugL
dotnet test  -c DebugL --no-build

dotnet build -c Release
```

Three configurations: `Debug`, `Release`, `DebugL`. **DebugL** pins every version to `999.0.0` and
prepends `../nuget` to `RestoreSources`; every packable project has `GeneratePackageOnBuild`, so a
build drops the `.nupkg` into `<project>/bin/DebugL/`.

⚠️ **Propagating a change to the consumers is three steps, and skipping any of them looks like the
change did not happen:**

1. Build (`dotnet build -c DebugL`). An **incremental** build can skip the pack target and leave a
   **stale `.nupkg` next to a fresh DLL** — always verify the package actually carries the new
   assembly (`unzip -p <nupkg> lib/net10.0/<Assembly>.dll | shasum` against the built one) before
   trusting a downstream test result.
2. Copy every `Meshmakers.*.999.0.0.nupkg` from `*/bin/DebugL/` into `dev/nuget/` — that is the feed
   the consumer repos restore from under DebugL (`Copy-NuGetPackages` in octo-tools does this).
3. Delete `~/.nuget/packages/<package>/999.0.0`. The global cache is checked before the local feed,
   so without this the consumer keeps building against the previous package no matter what is in
   `dev/nuget` (`Remove-GlobalNuGetPackages` in octo-tools).

Test projects: `Sdk.Common.Tests` (unit), `Sdk.Common.IntegrationTests`, and
`Sdk.Common.PipelineParityTests` — the last one enforces the Newtonsoft→System.Text.Json numeric and
scalar round-trip contract with Newtonsoft as the oracle. See `octo-construction-kit-engine/CLAUDE.md`
for the serialization rules themselves.

## Pipeline data flow

The pipeline data path is **System.Text.Json only** and goes through the path-only `IDataContext`
(`EtlDataPipeline/IDataContext.cs`). Nodes never see `JToken`/`JObject`/`JArray`, and node code must
not pass `JsonSerializerOptions` into any `IDataContext` method — the STJ details are internal to the
implementation. `SystemTextJsonOptions.Default` is the single options bundle; its
`UnsafeRelaxedJsonEscaping` encoder is load-bearing for every consumer that **hashes** serialized
output.

## Execution identity — what this repo owns

The adapter decides which identity a pipeline execution acts as (`PipelineIdentityResolver`, AB#5028,
in `octo-mesh-adapter`). What lives **here** is the carrier and the triggers that fill it:

- **`ExecutePipelineOptions.VerifiedPrincipal`** (AB#4975) — the caller a trigger authenticated. A
  slim, token-free value object, because the trigger projects it into the pipeline data root, which is
  echoed in HTTP responses, persistable by `SetPipelineExecutionResult@1` and shown in the Studio
  debug panel. Carries `PreferredChannel` (AB#5149, optional last positional parameter so every
  pre-existing constructor call keeps compiling): the user's preferred outbound channel for
  system-initiated messages, canonical uppercase `"TEAMS"` | `"SIGNAL"` (extensible), filled by the
  mesh adapter's verified-caller directory from the identity record and propagated verbatim — null
  for callers resolved from bearer claims (no such claim exists).
- **`ExecutePipelineOptions.CallerAccessToken`** (AB#5031) — the caller's **raw** bearer token, for
  nodes that must act as the caller against another service (the delegation / on-behalf-of grant needs
  it as `subject_token`). 🔴 It must never reach the data root, never `VerifiedPrincipal`, and never
  `IEtlContext.Properties` — that dictionary hangs on the `PipelineRegistration` and is shared across
  **all runs** of the pipeline, so a token left there would outlive its request. It is a
  **per-execution** side channel: trigger → ETL context of exactly this execution, nowhere else.
- Both surface on `IEtlContext` as **default interface members**, so an adapter implementing the
  interface itself was not broken by their addition.

### The identity ends at a pipeline chain — by decision, and visibly (AB#5045)

`ToPipelineDataEvent@1` → `FromPipelineDataEvent@1` crosses the message bus. The trigger on the far
side builds its `ExecutePipelineOptions` **without** a `VerifiedPrincipal` and **without** a caller
token, so an HTTP-triggered pipeline that chains to a second one runs the first half as the user and
the second half as the service identity.

🔴 **That is the decision, not an oversight.** Forwarding the identity would let a pipeline act as a
caller the *target* never authenticated: the sender picks the routing key, so whoever may enqueue into
the data flow would inherit whoever last triggered the sending pipeline — and on the fire-and-forget
path the message has no bounded lifetime, so the identity would stay usable for as long as it sits in
the queue. A privilege escalation is not something to introduce as a side effect of a chaining node.
If a chained execution should ever run as the user, the identity has to be **established** on the far
side (verified), never relayed.

What the decision costs is that one logical request runs under two identities, so the transition is
made **visible** rather than silent: `ToPipelineDataEventNode.RecordIdentityBoundary` writes the
hand-off to the execution log (`INodeContext.Info` — the channel the adapter and the Studio debug
panel already surface, deliberately not a new one), naming the subject whose identity ends there and
the target pipeline that will resolve its own. Deliberately **not** on the message: its payload is
pipeline data and no credential may travel on it. Without a caller identity the same site logs at
debug level — the overwhelming majority of chains are service-to-service and an info line per hop
would drown the case that matters.

The same holds for **`FromExecutePipelineCommand@1`** (Studio "Execute" and the ExecutePipeline API).
That is the row most likely to be "improved" later, because the person clicking the button *is*
authenticated — but the command travels over the bus and this node authenticates nobody, so a
forwarded identity would be an assertion the target cannot check.

`FromPipelineDataEventNodeTests` and `FromExecutePipelineCommandNodeTests` pin that both start their
execution with neither value, and `ToPipelineDataEventNodeTests` pins that the hand-off is recorded
and that neither the token nor the subject reaches the message. See the AB#5029 matrix in
`octo-mesh-adapter/CLAUDE.md` for how this row fits the other trigger kinds.

## Adapter hub authentication (AB#5072)

The adapter acquires its **own** client-credentials access token at startup and presents it on the
`/{tenantId}/adapterHub` SignalR connection. Four pieces, in the order the token travels:

1. **`AdapterOptions.IssuerUri` / `ClientId` / `ClientSecret`** (section `Adapter`, i.e. env
   `OCTO_ADAPTER__ISSUERURI`, `__CLIENTID`, `__CLIENTSECRET`). `IsEnabled` is `IssuerUri && ClientId`
   — the secret is deliberately not part of the check, so a confidential client with a missing secret
   fails loudly at the token endpoint instead of silently degrading to an anonymous connection that
   looks healthy until the gate is armed. The tenant is `AdapterOptions.DedicatedTenantId` — or, on a
   pool member, the **lending** tenant from `AdapterPool:PoolTenantId`; see below.
2. **`ConfigureAdapterAuthenticatorOptions`** (`IConfigureOptions<AuthenticatorOptions>`) projects
   those four onto the SDK's `AuthenticatorOptions`, which `AuthenticatorClient` reads.
3. **`AdapterAccessTokenService`** (`BackgroundService`) requests the token
   (`ApiScopes.OctoApiFullAccess`, `DefaultScopes.None` → exactly `octo_api`, no `offline_access`)
   and writes it into the singleton `IServiceClientAccessToken`.
4. **`AdapterHubClient`** was already handed that same instance. The SDK reads it through
   `HttpConnectionOptions.AccessTokenProvider` on every connection attempt, so a refresh reaches the
   next (re)connect with no notification path.

**What was broken.** Nothing filled that holder at startup. The only production writer was
`ServiceAccountTokenService.EnsureTokenAsync` in `octo-mesh-adapter`, and all of its callers sit on
pipeline **execution** paths (`DeployPipeline@1`, `AnthropicAiQuery@1`, `MeshContextCreatorService`,
`PipelineIdentityResolver`). At connect time the holder was empty, the provider returned `null`, the
connection went out with no `Authorization` header, and the hub saw an anonymous caller identified
only by the unprotected `adapter-rtId` / `adapter-ckTypeId` headers. Worse than plainly anonymous: an
adapter that had already run one of those pipelines *did* present a token on its **next** reconnect,
so the fleet was not in a deterministic state and the adapter-hub gate's (AB#5063) `LogOnly`
inventory was not worth reading.

🔴 **`acr_values=tenant:{TenantId}` is not optional.** Since AB#5077 a token request without it is
issued for the **system** tenant, and the controller then refuses the adapter on its own tenant route
with a 403. That is what `ConfigureAdapterAuthenticatorOptions` exists to pin.

🔴 **Unconfigured is a supported state and must stay one.** Every adapter in the estate runs without
these keys today. Without a client id the service logs one warning, never calls the authenticator,
and the access token stays null — which makes the SDK send no `Authorization` header and no
`access_token` query parameter at all, i.e. exactly today's connection. A hard requirement here would
take the whole adapter fleet down on upgrade.

**Renewal.** A live SignalR connection is authorized once, at connect. The *re*connect is the
exposure, and adapters reconnect routinely (controller rollout, node drain, network blip, the SDK's
own retry loop, a wake from scale-to-zero). Hence the loop: replace the token `RefreshSkew` (5 min)
before its own `exp`, retry every `RetryInterval` (30 s) after a failure, and **keep the previous
token on failure** — it is no less usable than none. The first acquisition runs inside `StartAsync`,
before `base.StartAsync`; hosted services start sequentially and this one is registered **before**
every other hosted service in both `AdapterBuilder` and `WebAdapterBuilder`, so the first hub
connection already carries a token rather than racing it.

⚠️ **`Adapter:IssuerUri` is a second key next to `octo-mesh-adapter`'s `Adapter:AuthorityUrl`.** Both
bind the same configuration section and name the same identity service, but `AuthorityUrl`
(`MeshAdapterConfiguration`, in the adapter repo) is the **inbound** issuer secured
`FromHttpRequest@2` routes accept. That type is unknown to this SDK and adapters without it (Loxone,
Modbus, Zenon, the simulation plug) still need an issuer, so the outbound credential carries its own
key. They normally hold the same value.

**Mostly not in this repo:** delivering the credentials into the adapter. The secret lives as a
`ServiceAccountConfiguration` in the tenant DB; the route runs through the `ValueOverride`s the
controller sends to the operator at deploy time and through the adapter chart. This repo's C# only
makes the adapter *able* to authenticate — but `src/charts/octo-plug-simulation` is itself one of
those adapter charts and carries the delivery half.

🔴 **Every SDK-based adapter chart has to render the three keys, or that adapter stays anonymous.**
The three env vars are read by `AdapterOptions`, which every adapter shares, so the controller
projects the credentials for *all* of them — but Helm silently ignores values a chart does not read.
Eight charts carry the block, deliberately byte-identical: `octo-mesh-adapter`,
`octo-loxone-adapter`, `octo-plug-simulation`, `octo-weclapp-adapter`, `octo-modbus-plug`,
`octo-modbus-socket`, `octo-finapi-adapter`, `octo-eda-adapter`. A new adapter chart must copy it,
and `authUri` needs no plumbing — the operator writes it into every workload's context values
(`WorkloadContextValuesBuilder`). The value paths the controller writes are
`serviceAccountClientId` and the secret-flagged `secrets.serviceAccountClientSecret`; they are
pinned on the controller side in `PoolService`.

Tests: `tests/Sdk.Common.Tests/Adapters/AdapterAccessTokenServiceTests.cs` (token published into the
shared holder, scope shape, unconfigured never calls the authenticator in either direction, valid
token not re-acquired, refresh-window replacement, failure keeps the previous token, tokenless
response not published, `StartAsync` acquires before returning in both shapes, and neither secret nor
token in the **rendered** log output) and `ConfigureAdapterAuthenticatorOptionsTests.cs`.

## Tenant isolation — `AdapterOptions.TenantId` is gone (AB#4924, increment 3)

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
which every execution already passes through, so the mechanism is live for the whole fleet today.
On a dedicated adapter the value equals the adapter's own tenant on every execution, which is why
this increment is behaviour-preserving; leasing changes only the value, never this site.

- A **singleton carrying an `AsyncLocal`**, not a scoped service: executions run concurrently and a DI scope does not flow across an async call chain, whereas the tenant of an execution must. The `AsyncLocal` is an **instance** field — a static one would couple two hosts sharing a process.
- `TenantId` **throws** outside an execution rather than returning null or a default. A caller reaching for the tenant outside one has a bug, and the outcome that must never happen is a plausible-looking wrong answer.
- Nested `BeginExecution` **restores** the outer tenant on dispose, so a node starting a sub-pipeline does not strip the tenant off the rest of the outer pipeline.
- `IsPoolMember` is hard-coded `false` and deliberately **not** bindable — increment 3 ships for the fleet to bake before any lease exists, and an environment variable that could flip it would defeat that.

⚠️ **`OCTO_ADAPTER__TENANTID` is still bound for one release** by `ConfigureLegacyAdapterTenantId`,
with a deprecation warning naming the adapter. Eight Helm charts set that key, and the options binder
ignores a key with no matching property **silently** — without the shim every one of those adapters
would have fallen back to the `"meshTest"` default and connected on the wrong tenant route, reporting
nothing at startup. The new key is `OCTO_ADAPTER__DEDICATEDTENANTID` and wins when both are set.

**Tests** (`tests/Sdk.Common.Tests/TenantIsolation/`, 12 of them): the reflection guard that
`AdapterOptions` carries no member named `TenantId` by any route; a DI sweep asserting every SDK
singleton whose surface mentions a tenant is on a cleared allow-list **with a reason**; concurrent
interleaving over 12 tenants; a poison canary; 200 randomised interleavings. They were checked
against a deliberate mutation — clearing instead of restoring in `Restore.Dispose` makes the nesting
test fail.

🔴 **What these tests are not.** They exercise the *scope mechanism* under concurrency, not a real
two-tenant pipeline execution. ✅ **The full interleave suite arrived with increment 6** and lives in
`octo-mesh-adapter` (`tests/MeshAdapter.Sdk.IntegrationTests/Leasing/LeasedTenantIsolationTests`):
pipeline output over two real tenant databases, the poison canary, the post-release state, the
log-target and identity assertions, plus the mesh-side DI sweep where the caches actually live.

## Adapter pool membership (AB#4924 increment 6)

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

### 🔴 A pool member carries no `DedicatedTenantId` — enforced, not assumed (AB#4924)

`ConfigurePoolMemberAdapterTenantId` (`IPostConfigureOptions<AdapterOptions>`, registered by
`AddAdapterPoolMember()`) **clears** `AdapterOptions.DedicatedTenantId` whenever the pool
configuration is complete, and logs one warning if something had configured it.

It exists because "it is null on a pool member" was a claim three call sites in two repositories rely
on and that nothing made true: `AdapterOptions`' constructor sets `"meshTest"`, and the AB#4924 local
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

### `AdapterPoolTenantScope` — two nested notions of "the current tenant"

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

### `AdapterPoolClient` — the ordering is the invariant

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
- Both directions degrade through the once-only `HubException` pattern (same as AB#4917's scale-status
  channel): controller, `octo-sdk` and this SDK ship together, so it only covers a rolling upgrade.

### Registration carries the member's node descriptors (AB#4924)

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

### `IAdapterLeaseParticipant` — the isolation invariant, made composable

The SDK cannot see the caches an adapter repository owns, so each of them registers a participant
instead of the SDK carrying a hard-coded list it would have to keep in step. "Did this member really
drop everything" becomes a question a test can answer by enumerating participants.
`octo-mesh-adapter` registers three: the borrower identity, the CK model cache and the pipeline
registry.

⚠️ `IAdapterLeaseWorkItem` defaults to `NoAdapterLeaseWorkItem` — an honest "nothing queued for me",
because the queue and the scheduler are increment 7. The member still runs the full enter/leave/release
cycle, which is what makes the wire contract verifiable before the scheduler exists.

Tests: `tests/Sdk.Common.Tests/TenantIsolation/AdapterPoolTenantScopeTests.cs`,
`tests/Sdk.Common.Tests/Adapters/AdapterPoolClientTests.cs`,
`tests/Sdk.Common.Tests/TenantIsolation/OrchestratorRequiresTheTenantScopeTests.cs`, and the
pool-member arm of `SingletonTenantFreedomSweepTests`.

## 🔴 `EtlDataOrchestrator` now *requires* `IAdapterTenantScope` (AB#4924 increment 6)

Increment 3 resolved it with `GetService` so hosts that do not build on `AdapterBuilder` kept working
while the refactor baked. That tolerance is **gone**: on a pool member a missing scope means every
execution silently runs with no tenant entered.

Removing it is only safe because **the registration moved into `AddDataPipeline()`**, the same call
that registers the orchestrator — `TryAddSingleton`, so a pool member that registered the lease-aware
scope first is not overwritten. Before the move, `IAdapterTenantScope` was registered only in
`AdapterBuilder` / `WebAdapterBuilder`, and `GetRequiredService` would have broken
`octo-adapter-sap`'s `Program.cs`, `octo-plug-zenon`'s `AdapterInstanceEntryPoint`, both samples here,
and roughly a dozen test fixtures across this repo, `octo-mesh-adapter` and `octo-adapter-weclapp`.

## Trigger execution class (AB#4924 increment 4)

A trigger node declares the scheduling class it implies when its pipeline runs on a leased adapter
pool. Two classes only — `Interactive` (key 0) and `Batch` (key 1) — never a numeric priority: a
number needs somebody to assign it and makes "why is my job not running" unanswerable in a queue
view.

```csharp
[NodeName("FromHttpRequest", 2)]
[NodeExecutionClass(PipelineExecutionClass.Interactive)]
public record FromHttpRequestNodeConfiguration2 : TriggerNodeConfiguration { … }
```

Mechanics mirror `RequiresRunningProcess` exactly: the attribute goes on the node **configuration**
record, `NodeSchemaRegistry.BuildDescriptor` picks it up by reflection, and it travels on
`NodeDescriptor` / `NodeDescriptorDto.ExecutionClass`. Follow `NodeDeprecatedAttribute` rather than
`NodeRequiresRunningProcessAttribute` when adding more of these — it is a parameterized attribute,
not a marker.

- 🔴 **Append new descriptor fields last, with a default.** `NodeDescriptorDto` is the wire contract between an adapter and the controller; a trailing defaulted parameter is what lets an older adapter's payload still deserialize on a newer controller.
- The enum values are the keys of the `PipelineExecutionClass` CK enum in `System.Communication` and must stay in lockstep — they are persisted on every pipeline entity. They are ordered so ascending value equals scheduling order.
- Absent means `Batch`, the conservative answer: a trigger nobody classified must never jump a queue.
- The schema extension `x-executionClass` is emitted for **every trigger**, including the default — unlike `x-requiresRunningProcess`, which is emitted only when true. An editor showing the class only for nodes that opted in could not distinguish "classified Batch" from "not classified", which is the question the extension exists to answer. Non-triggers get nothing.
- Currently `Interactive`: `FromHttpRequest@1`, `FromHttpRequest@2` (both in `octo-mesh-adapter`), `FromExecutePipelineCommand@1`. Everything else relies on the default.

## Node inventory (`src/Sdk.Pipeline/EtlDataPipeline/Nodes/`)

- **Triggers**: `FromPipelineDataEvent@1`, `FromExecutePipelineCommand@1`, `FromPolling@1`
- **Control**: `ForEach`, `For`, `Group`, `If`, `Switch`, `ObjectIterator`, `SelectByPath`
- **Extracts**: `SetJson`, `SetPrimitiveValue`, `SetArrayOfPrimitiveValues`,
  `GetPipelineConfigByWellKnownName`
- **Transforms**: `Concat`, `FormatString`, `Map`, `Project`, `Flatten`, `Join`, `Distinct`, `Math`,
  `LinearScaler`, `Hash`, `Base64Encode`/`Decode`, `ConvertDataType`, `DateTime`, `TransformString`,
  `ExecuteCSharp`, `Logger`, `PrintDebug`, `SumAggregation`
- **Loads**: `ToPipelineDataEvent@1`, `ToWebhook@1`, `SetPipelineExecutionResult@1`
- **Buffering**: `Buffer`, `BufferRetrieval`

Domain nodes (entity CRUD, stream data, HTTP, files, PDF, mail, …) live in `octo-mesh-adapter`, not
here.

### ExecuteCSharp argument typing (AB#5232)

`ExecuteCSharp@1` arguments are declared in the generated script with the real C# type for their
`DataType`: the scalars map to `string`/`int`/`long`/`bool`/`double`/`DateTime`, and the **array**
kinds map to typed CLR arrays — `StringArray → string[]`, `IntArray`/`IntegerArray` → `int[]`,
`RecordArray → object[]` (elements stay `JsonElement` for complex records). Those three are ALL the
array kinds `AttributeValueTypesDto` defines; everything else falls back to `object`. Incoming values
are materialized into those arrays regardless of shape (typed array, `JsonElement`/`JsonNode` array,
or a native list from a fixed configuration `Value`), and array `ReturnType`s materialize `T[]`,
`List<T>` and lazy LINQ enumerables alike. Before AB#5232 array arguments were declared `object` and
arrived as a boxed `JsonElement`, so `foreach` over them failed to compile — pipeline scripts written
against that era cast the argument via `((JsonElement)arg).EnumerateArray()`; such scripts must be
made version-tolerant (check `arg is JsonElement` and fall back to the typed array) while pre-fix
adapter images are still deployed. Null/absent arguments still coalesce to the type's default
(`null` for arrays).

## Development Notes

- Target framework `net10.0` only; `netstandard2.0` was dropped platform-wide in Phase 3.
- Nullable reference types are on and **warnings are errors**.
- The pipeline definition deserializer is **YamlDotNet**: a key that is *present and null* overwrites a
  C# property initializer, so node-configuration numbers and enums that need a default must be
  nullable with the default resolved where they are read (the `JsonNullAsDefaultAttribute` used for
  GlobalConfiguration settings records only covers the System.Text.Json path).
- Helm charts under `src/charts` follow the AB#4948 lane rule: `test/*` publishes into the shared dev
  bucket as a SemVer **prerelease**, so an unpinned `ChartVersion` keeps resolving main's newest
  stable chart.
