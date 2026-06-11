# octo-communication-sdk

OctoMesh **Communication SDK** — the adapter framework, ETL pipeline, and runtime infrastructure used to build adapter services that connect external systems to the OctoMesh platform.

This repository was carved out of [`octo-sdk`](https://github.com/meshmakers/octo-sdk) in Phase 3 of the YAML pipeline migration. The split separates the *client SDK* (REST clients, contracts — stays in `octo-sdk`) from the *adapter / pipeline infrastructure* (lives here).

## Layout

```
src/
├── Sdk.Adapters/             — adapter base classes (was Sdk.Common/Adapters)
├── Sdk.Pipeline/             — ETL pipeline + node framework + execution services
│                               (was Sdk.Common/EtlDataPipeline + Sdk.Common/Services)
├── Sdk.CommunicationAdapter/ — Generic Host + DI bootstrap for hosting adapters
├── Sdk.Plug.Simulation/      — sample simulation adapter
└── Sdk.SimulationNodes/      — pipeline-node implementations for simulation
```

## Dependency direction

```
octo-distributedEventHub
        ↓
octo-construction-kit-engine
        ↓
   octo-sdk    (Communication.Contracts, Sdk.ServiceClient, Sdk.Common/Encryption stay here)
        ↓
octo-common-services
        ↓
octo-communication-sdk    ← this repo
        ↓
(consumers: mesh-adapter, eda-adapter, loxone, mqtt, sap, finapi,
 modbus, demos, communication-controller-services, communication-operator)
```

## Build

```bash
# Production
dotnet build Octo.CommunicationSdk.sln -c Release

# Local dev (reads NuGets from ../nuget — populated by `invoke-buildall -configuration DebugL`)
dotnet build Octo.CommunicationSdk.sln -c DebugL
```

## Release

Releases are driven by `release-communication-train.yml` in `octo-mesh-deployment` (Phase 5 of the migration). The train tags `r<X.Y.Z>` on this repo and queues the CI on the tag. Versioning follows the A2 Layered strategy: this repo carries its own version line (`comm-X.Y.Z`) and pins to a Libs major-minor via `OctoVersion` in `Directory.Build.props`.

## See also

- Architecture concept: `octo-mesh-deployment/docs/pipeline-architecture-concept.md`
- Original SDK: [octo-sdk](https://github.com/meshmakers/octo-sdk)
