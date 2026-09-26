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
