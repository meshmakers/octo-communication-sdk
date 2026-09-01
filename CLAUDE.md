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
  debug panel.
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
