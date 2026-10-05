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

### Integer coercion on typed reads (AB#5275)

`NewtonsoftParityDoubleConverter` (CK engine) deliberately writes an integral `double` as `5.0`, not
`5` — otherwise the value round-trips back as `Int64` and lands in MongoDB as a `BsonInt64` where
Newtonsoft stored a `BsonDouble`. STJ's **built-in** Int32/Int64 converters, however, match on the
number token's **raw text**, so they rejected that same `5.0`. Every "a node wrote a double, a later
node reads it as Int" pipeline therefore failed — `ConvertDataType@1`, `If@1`, `Switch@1`, `For@1`,
`DateTime@1`, `SetPrimitiveValue@1`, `ExecuteCSharp@1` and the adapter's domain nodes alike.

`NewtonsoftParityInt32Converter` / `…Int64Converter` (`EtlDataPipeline/NewtonsoftParityIntegerConverters.cs`)
are the **read-side twins** registered in `SystemTextJsonOptions.Default`, so one registration fixes
every `Deserialize<T>` path at once — including `int?`/`long?` via STJ's nullable factory, `int[]`,
`List<int>` and `int` members of DTOs. Values coerce with **banker's rounding** (`5.0`→5, `5.7`→6,
`5.5`→6, `4.5`→4); out-of-range throws `JsonException` and **not** `OverflowException`, because
`DateTimeNode` catches the former to build `InvalidUnixTimestamp`.

🔴 **`JsonScalar.ToClr` stays strict on purpose.** That is the *dynamic* boxing path behind
`IDataContext.GetValue()` and `RtAttributesConverter`; its contract is "reals stay double", and
coercing there would bring the BsonInt64 regression straight back. Only explicitly typed reads
changed. `DataContextIntegerCoercionTests.DynamicBoxingPath_StaysDouble` is the guard.

The rounding semantics are not a design choice but an **empirical** one:
`Sdk.Common.PipelineParityTests.IntegerCoercionParityTests` consults Newtonsoft as the oracle at
runtime, so the rule cannot drift. Two divergences are deliberate and pinned there — a quoted real
(`"5.0"`) coerces here but throws in Newtonsoft, and a JSON boolean throws here but yields `1` in
Newtonsoft.

### What a node's `…Path` setting may point at (AB#5351)

Every node setting read through **`GetArray<T>`** (`toPath`, `rtIdsPath`, `opsPath`, `path`, …)
accepts four shapes, and a pipeline author picks whichever the upstream data has:

| Shape | Example | Result |
|---|---|---|
| array | `$.ids` → `["a","b"]` | one entry per element |
| scalar | `$.id` → `"a"` | widened to a **single-entry** array |
| multi-match path | `$.Items[*].RtId`, `$..RtId`, `$.Items[?(@.Kind=='Doc')].RtId` | one entry per match, document order |
| absent / null / object / no match | `$.nope` | **`null`** — not an empty sequence |

The node turns that `null` into its own error, so a message like *"No RtIds found at path …"* means
the path matched nothing — not that the path form is unsupported.

🔴 The multi-match row is the one that was missing. A single-value read resolves a path with
`JsonPathWalker.Select(...).FirstOrDefault()`, so before AB#5351 a wildcard path silently returned
**only the first match** while the overlay was still clean and `null` once any node had written to
the document — the same pipeline, two wrong answers, depending on what ran before it. `GetArray<T>`
therefore classifies the path first (`JsonPathShape.IsMultiMatch`) and collects multi-match paths
through `SelectMatches`. The classifier's pre-filter is a plain character test, so an ordinary
property/index path never pays a parse and the single-value fast paths stay allocation-identical —
which `TypedGetAllocationGate` and `DataContextBigDocReadAllocationGate` pin. **`Get<T>` is
unchanged and still resolves to the first match**; only `GetArray<T>` is multi-match aware.

⚠️ **Inside `ForEach@1` a multi-match path sees the child's own document only** — its writes
(`$.key…`) and its aliases (`$.full…`), never the parent-fallback chain, because folding the parent
in would re-materialise the whole outer document per call (the allocation alias pruning removed,
AB#4662). So reach the outer document through `$.full[*]…`, not through a bare outer path: the
single-value read of that bare path resolves through the parent, the multi-match read returns
`null`. That boundary is older than AB#5351 (it is `SelectMatches`/`UpdateMatchesAsync` semantics)
and is pinned by `DataContextGetArrayMultiMatchTests`.

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

## The trigger → pipeline status line (AB#5385)

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

### The start is reported before the context exists (AB#5493)

`AdapterTriggerContext.StartExecutePipelineAsync` generates the execution id, captures the start
time and calls `IPipelineExecutionReporter.ReportExecutionStartAsync` **before**
`IContextCreatorService.CreateEtlContext`. Context creation (and the debugger setup behind it) is
wrapped in a try/catch that logs at Error with the tenant id, reports
`ReportExecutionEndAsync(Failed, ex.Message)` under the same id and rethrows. It used to be the
other way round, and a context creation that throws (prod-1: the mesh adapter's
`FindTenantRepositoryAsync` failing with "System tenant database does not exist" on every cron
tick for four days) never reached the reporter — no execution on the controller, frozen
`PipelineStatistics`, `octo.pipeline.execution.failures` at zero, no alert. No double report:
`RegisterExecution` runs only after the try, so `EndExecutePipelineAsync` is never called for a
failed id, and a pipeline that fails *after* the start still ends exactly once through the
existing end path. The mesh adapter's hand-maintained copy carries the same order — keep them in
step. Tests: `AdapterTriggerContextTests` (`StartExecutePipelineAsync_WhenContextCreationFails_*`,
`StartExecutePipelineAsync_ReportsTheStartBeforeTheContextIsCreated`).

## Adapter hub authentication (AB#5072)

The adapter acquires its **own** client-credentials access token at startup and presents it on the
`/{tenantId}/adapterHub` SignalR connection. Four pieces, in the order the token travels:

1. **`AdapterOptions.IssuerUri` / `ClientId` / `ClientSecret`** (section `Adapter`, i.e. env
   `OCTO_ADAPTER__ISSUERURI`, `__CLIENTID`, `__CLIENTSECRET`). `IsEnabled` is `IssuerUri && ClientId`
   — the secret is deliberately not part of the check, so a confidential client with a missing secret
   fails loudly at the token endpoint instead of silently degrading to an anonymous connection that
   looks healthy until the gate is armed. The tenant is the already-present `AdapterOptions.TenantId`.
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

## Adapter hub registration is the only "reachable" signal (AB#5409)

A live SignalR connection is **not** the same as a usable adapter. Registration at the controller's
`adapterHub` is a hub invoke of its own, and it can fail while the connection is up. When it does,
the adapter is *deaf*: RabbitMQ data events and in-process triggers keep running, so pipelines it
already knows about still execute, but everything the controller has to **push** — configuration
updates, `DeployDataFlow`, HTTP-activator calls — goes nowhere. On prod-1 five of seven tenant
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
  (15 min) without a registration, so the container restarts and re-registers. On prod-1 a restart
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

🔴 **`AdapterHubRecoveryService` stops the host by decision only, never by a failing check**
(AB#5473). An exception that escapes a `BackgroundService` stops the host too, so the service that
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

### Registration retry on the (re)connect path

`AdapterExecutionService.RegisterAtHubAsync` retries the register invoke `RegistrationMaxAttempts`
(3) times with a linear back-off (`RegistrationRetryBaseDelay`, 2 s × attempt), then rethrows so the
SignalR (re)connect loop keeps its own retry (AB#4805 — swallowing it made the loop treat a failed
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
log away within the hour, which is why this was missed twice (AB#5409 item 3, a different layer).

Tests: `Sdk.Common.Tests/Adapters/AdapterExecutionServiceTests` (a reconnect whose first registration
throws ends registered; all attempts failing marks not-registered and rethrows) and
`Sdk.Common.Tests/Adapters/AdapterHubRegistrationReadinessTests` (readiness matrix, recovery timing,
never-registered guard, and a connection state that cannot be read: it counts as an outage, the
service keeps sampling and restarts only after the timeout).

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

#### A JSON type that does not match the declared `dataType` (AB#5463)

Argument resolution runs **before** the script, so a deserializer throw there is invisible to any
`try`/`catch` inside the script and — with no `continueOnError` on `ExecuteCSharp@1` or the
per-item `ForEach@1` — kills the whole iteration. The production case was an LLM emitting
`documentNumber` as an unquoted JSON number for an argument declared `String`: STJ has no
number→string coercion, so `Get<string>` threw.

`ResolveTypedFromPath` therefore tries the strict typed read first (fast path unchanged) and only
on `JsonException`/`FormatException` falls back to `ResolveLeniently`: number→`String` becomes the
raw token text, `"true"`→`Boolean` parses, numeric strings parse **invariant**, and the array kinds
convert element-wise. Every fallback conversion logs a **warning** naming the argument and both
types. A value that genuinely cannot be converted (an object where a `Double` is declared) still
throws, now as a `PipelineExecutionException` that names argument, path, declared type and found
kind — a silent null would be worse than the throw.

🔴 Two things are deliberate. The coercion sits in the **node**, not in `SystemTextJsonOptions.
Default` — that bundle is shared by the whole engine and every adapter, and a string converter
there would change behaviour far outside this node. And **`DateTime` stays strict**: STJ already
reads every ISO 8601 string, and whatever it rejects (`"01.02.2026"`, a bare Unix number) is
ambiguous; `DateTime.Parse` under the invariant culture would guess a month/day order rather than
fail. Note that numeric strings into `Double`/`Int` already parsed before AB#5463 —
`SystemTextJsonOptions.Default` inherits `NumberHandling.AllowReadingFromString` from the CK
engine's `RtSystemTextJsonSerializer.CreateDefault()`, and the parity converters honour it.

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
