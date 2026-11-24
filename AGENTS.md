# AGENTS.md

Instructions for coding agents in this repository. Detail lives in `docs/`; read the matching file before changing the
code it covers.

## What this repo is

**octo-communication-sdk** is the framework every OctoMesh adapter is built on: the ETL pipeline
engine, the node model, the trigger/execution contracts, and the adapter host. It has no adapter of
its own — it is consumed as NuGet packages by `octo-mesh-adapter`, `octo-adapter-loxone`,
`octo-plug-zenon` and the other adapter repos, so **a change here is a change to every adapter**.

The namespaces still read `Meshmakers.Octo.Sdk.Common.*` on purpose: they stay stable so that consumer
repositories need no source change beyond a package reference.

### Projects

| Project | Package | What it is |
|---|---|---|
| `src/Sdk.Pipeline` | `Meshmakers.Octo.Sdk.Pipeline` | The ETL pipeline and node framework: `EtlDataPipeline/` (orchestrator, nodes) and `Services/` (execution contracts). |
| `src/Sdk.Adapters` | `Meshmakers.Octo.Sdk.Adapters` | The adapter host: `AdapterBuilder`, startup/shutdown, the controller hub callback, health and metrics. |
| `src/Sdk.CommunicationAdapter` | `Meshmakers.Octo.Sdk.CommunicationAdapter` | The `System.Sdk` CK model and the adapter contracts that go with it. |
| `src/Sdk.Common.Web` | `Meshmakers.Octo.Sdk.Common.Web` | ASP.NET Core hosting for sockets and plugs. |
| `src/Sdk.SimulationNodes` | `Meshmakers.Octo.Sdk.SimulationNodes` | Simulation pipeline nodes and their generators. |
| `src/Sdk.Plug.Simulation` | *(executable)* | The plug-simulation service; publishes its own Helm chart. |

`samples/` holds `Sdk.Plugs.Sample` and `Sdk.Socket.WebSample`; neither is packaged.

## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/Sdk.Adapters/AdapterOptions.cs`, `src/Sdk.Adapters/AdapterAccessToken*.cs`, `src/Sdk.Adapters/ConfigureAdapterAuthenticatorOptions.cs`, `src/charts/**` | `docs/adapter-hub-authentication.md` |
| `src/Sdk.Adapters/{AdapterHub*,AdapterExecutionService,HostedAdapterExecutionService,AdapterBuilder}.cs` | `docs/adapter-hub-registration.md` |
| `src/Sdk.Adapters/AdapterPool*.cs`, `src/Sdk.Adapters/IAdapterLease*.cs`, `src/Sdk.Adapters/AdapterNodeDescriptorProjection.cs` | `docs/adapter-pool-membership.md` |
| `src/Sdk.Pipeline/Services/{ExecutePipelineOptions,VerifiedPrincipal}.cs`, `src/Sdk.Pipeline/EtlDataPipeline/Nodes/{Triggers/From*,Loads/ToPipelineDataEvent}Node.cs` | `docs/execution-identity.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/Nodes/**`, `src/Sdk.Pipeline/EtlDataPipeline/DefaultPipelineLogger.cs` | `docs/nodes.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/**`, `tests/Sdk.Common.PipelineParityTests/**` | `docs/pipeline-data-flow.md` |
| `src/Sdk.Adapters/{AdapterPool*,HubRegistration*}.cs`, `**/PipelineRegistryService.cs`, `tests/**/AdapterPool*` | `docs/pool-member-recovery.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/PipelineSecretValues.cs`, `src/Sdk.Pipeline/EtlDataPipeline/Nodes/**` | `docs/secret-values-in-pipelines.md` |
| `src/Sdk.Pipeline/Services/{*TenantScope*,IAdapterLeaseScope}.cs`, `src/Sdk.Adapters/Configure*AdapterTenantId.cs`, `tests/**/TenantIsolation/**` | `docs/tenant-isolation.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/Configuration/NodeExecutionClassAttribute.cs`, `src/Sdk.Pipeline/EtlDataPipeline/Nodes/Triggers/**` | `docs/trigger-execution.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/AdapterTriggerContext.cs`, `src/Sdk.Pipeline/Services/*Reporter*.cs`, `src/Sdk.Pipeline/Services/PipelineExecution*.cs` | `docs/trigger-status-reporting.md` |
<!-- <<< end generated: routing -->

Background, no path trigger: [docs/pipeline-and-adapter-framework.md](docs/pipeline-and-adapter-framework.md) (ETL
pipeline orchestration, `IDataContext`, pipeline schema generation, parity and allocation test suites).

## Build & test

```bash
# Local development — 999.0.0, restores from ../nuget
dotnet build -c DebugL
dotnet test  -c DebugL --no-build

dotnet build -c Release
```

Three configurations: `Debug`, `Release`, `DebugL`. **DebugL** pins every version to `999.0.0` and
prepends `../nuget` to `RestoreSources`; every packable project has `GeneratePackageOnBuild`, so a
build drops the `.nupkg` into `<project>/bin/DebugL/`. Outside DebugL the upstream versions come only
from the pipeline: there is no release-line `OctoVersion` fallback in `Directory.Build.props`,
so `dotnet build -c Release` needs `-p:OctoVersion=X.Y.Z` (or `-p:OctoNugetPrivateServer=<feed>` for
the main line `0.1.*`) and otherwise fails fast with `OCTO0001`.

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
scalar round-trip contract with Newtonsoft as the oracle. The serialization rules themselves live in the
`octo-construction-kit-engine` repository (`octo-construction-kit-engine/docs/ck-schema-and-json-serialization.md`).

## Before you commit

1. `dotnet build -c DebugL` and `dotnet test -c DebugL --no-build` are green.
2. A change a consumer repository has to see is propagated with the three steps under Build & test; a green local
   build alone does not prove the consumer builds against the new package.
3. The matching file under `docs/` (and this file when a rule changes) is updated in the same commit: a new node, a
   new service or a change to a contract written down there.

## Rules

These rules bind new and changed code. Known exceptions in existing code are named in the linked docs.

- Target framework `net10.0` only; nullable reference types are on and warnings are errors.
- **Pipeline data is System.Text.Json and path-only.** Nodes use `IDataContext`, no Newtonsoft types and no
  `JsonSerializerOptions` on an `IDataContext` call; dynamic boxing goes only through `JsonScalar.ToClr` without
  integer coercion, and a change to typed integer reads keeps `Sdk.Common.PipelineParityTests` green, including its
  pinned divergences. See `docs/pipeline-data-flow.md`.
- **Caller tokens live for one execution.** `CallerAccessToken` and `MethodInvocation` never reach the data root or
  `IEtlContext.Properties`, nor does the token reach `VerifiedPrincipal`; no credential or message body goes into a
  trigger status line, and no credential into a log line or exception text. See `docs/execution-identity.md` and
  `docs/trigger-status-reporting.md`.
- **A node that puts a plaintext secret into the data context or resolves a credential calls
  `INodeContext.RegisterSecret` before any log line or output**; only output through `NodeContext` is masked, an
  injected `ILogger` is not. See `docs/secret-values-in-pipelines.md`.
- **A pipeline's identity is not relayed over the message bus.** `FromPipelineDataEvent` (and any new trigger fed by
  another pipeline) starts without `VerifiedPrincipal` and caller token; only `FromExecutePipelineCommand` carries the
  invoker that the controller verified, under its `CallerBindingMode`. See `docs/execution-identity.md`.
- **Tenant**: new code takes the tenant from `IEtlContext.TenantId` or `INodeContext` inside a node and from
  `IAdapterTenantScope` around it, never from a process-wide value. `AdapterOptions.DedicatedTenantId` is only for
  the adapter's own connection, and `AdapterOptions` gets no `TenantId` member back. A new singleton that mentions a
  tenant goes on the allow-list of the DI sweep test with a reason (the named exception). See
  `docs/tenant-isolation.md`.
- **Pool member**: `AddAdapterPoolMember()` is called before `AddDataPipeline()`; `AdapterHubRegistration` is not
  registered on a member, so the `AdapterHubRegistration` health check (`AdapterHubReadinessHealthCheck`) never runs
  there; readiness follows the registration, never a lease; a member does not start trigger nodes. See
  `docs/adapter-pool-membership.md` and `docs/pool-member-recovery.md`.
- **Liveness stays independent of the hub**: no health check of this repository gets the `live` tag, and the recovery
  services stop the host by decision, never by a failing check. See `docs/adapter-hub-registration.md`.
- **Compatibility with adapter repositories**: a member added to an interface or base class they implement
  (`IEtlContext`, `INodeContext`, `IPipelineExecutionReporter`, `IPipelineLogger`, `TriggerContext`) is a default
  interface member or a virtual no-op. A persisted enum such as `DateTimeOperationDto` only gets values appended, and
  `PipelineExecutionClass` stays in lockstep with its CK enum.
- **Adapter charts**: a new or changed adapter chart renders the three adapter credential keys. See
  `docs/adapter-hub-authentication.md`.
- **Pipeline definitions are read with YamlDotNet**: a key that is present and null overwrites a C# property
  initializer, so node-configuration numbers and enums that need a default are nullable, with the default resolved
  where they are read (`JsonNullAsDefaultAttribute` in octo-mesh-adapter covers only the JSON path). See
  `docs/nodes.md`.
- **This repository is public.** No work item ids, internal hostnames, customer or tenant names, or secrets in docs
  and in new or changed code comments and test names (work item ids stay in `CHANGELOG.md` entries); remove an
  existing one when you touch its line.
- **A released node version keeps its behaviour.** A change that alters what `Name@N` does ships as `Name@N+1`; a new
  option is optional with a neutral default and inert unless configured, and a new operation value is appended. See
  `docs/nodes.md`.
