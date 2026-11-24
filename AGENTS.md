# AGENTS.md

Instructions for coding agents in this repository. Detail lives in `docs/`; read the matching file before changing the
code it covers.

## What this repo is

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

## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/Sdk.Adapters/AdapterOptions.cs`, `src/Sdk.Adapters/AdapterAccessToken*.cs`, `src/Sdk.Adapters/ConfigureAdapterAuthenticatorOptions.cs`, `src/charts/**` | `docs/adapter-hub-authentication.md` |
| `src/Sdk.Adapters/AdapterHub*.cs`, `src/Sdk.Adapters/AdapterExecutionService.cs`, `src/Sdk.Adapters/HostedAdapterExecutionService.cs`, `src/Sdk.Adapters/AdapterBuilder.cs` | `docs/adapter-hub-registration.md` |
| `src/Sdk.Adapters/AdapterPool*.cs`, `src/Sdk.Adapters/IAdapterLease*.cs`, `src/Sdk.Adapters/AdapterNodeDescriptorProjection.cs` | `docs/adapter-pool-membership.md` |
| `src/Sdk.Pipeline/Services/ExecutePipelineOptions.cs`, `src/Sdk.Pipeline/Services/VerifiedPrincipal.cs`, `src/Sdk.Pipeline/EtlDataPipeline/Nodes/Triggers/From*Node.cs`, `src/Sdk.Pipeline/EtlDataPipeline/Nodes/Loads/ToPipelineDataEventNode.cs` | `docs/execution-identity.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/Nodes/**`, `src/Sdk.Pipeline/EtlDataPipeline/DefaultPipelineLogger.cs` | `docs/nodes.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/**`, `tests/Sdk.Common.PipelineParityTests/**` | `docs/pipeline-data-flow.md` |
| `src/Sdk.Adapters/AdapterPool*.cs`, `src/Sdk.Adapters/HubRegistration*.cs`, `src/Sdk.Pipeline/Services/PipelineRegistryService.cs`, `tests/Sdk.Common.Tests/Adapters/AdapterPool*` | `docs/pool-member-recovery.md` |
| `src/Sdk.Pipeline/EtlDataPipeline/PipelineSecretValues.cs`, `src/Sdk.Pipeline/EtlDataPipeline/Nodes/**` | `docs/secret-values-in-pipelines.md` |
| `src/Sdk.Pipeline/Services/*TenantScope*.cs`, `src/Sdk.Pipeline/Services/IAdapterLeaseScope.cs`, `src/Sdk.Adapters/Configure*AdapterTenantId.cs`, `tests/Sdk.Common.Tests/TenantIsolation/**` | `docs/tenant-isolation.md` |
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
from the pipeline (AB#6297): there is no release-line `OctoVersion` fallback in `Directory.Build.props`,
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
scalar round-trip contract with Newtonsoft as the oracle. See `octo-construction-kit-engine/CLAUDE.md`
for the serialization rules themselves.

## Before you commit

> **Important**: Keep this file up to date when the codebase changes — new nodes, new services, or a
> change to one of the contracts written down below.

## Rules

- Target framework `net10.0` only; `netstandard2.0` was dropped platform-wide in Phase 3.
- Nullable reference types are on and **warnings are errors**.
- The pipeline definition deserializer is **YamlDotNet**: a key that is *present and null* overwrites a
  C# property initializer, so node-configuration numbers and enums that need a default must be
  nullable with the default resolved where they are read (the `JsonNullAsDefaultAttribute` used for
  GlobalConfiguration settings records only covers the System.Text.Json path).
- Helm charts under `src/charts` follow the AB#4948 lane rule: `test/*` publishes into the shared dev
  bucket as a SemVer **prerelease**, so an unpinned `ChartVersion` keeps resolving main's newest
  stable chart.
