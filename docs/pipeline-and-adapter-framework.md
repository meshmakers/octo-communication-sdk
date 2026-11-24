---
description: ETL pipeline orchestration, IDataContext, pipeline schema generation and the parity and allocation test suites.
background: true
---

# Pipeline and adapter framework

Source: moved verbatim from `octo-sdk` `docs/pipeline-and-adapter-framework.md` (octo-sdk main `0d24a31`).

> This describes code of `octo-communication-sdk` (`src/Sdk.Pipeline`, `src/Sdk.Adapters`, `src/Sdk.Plug.Simulation`,
> `tests/Sdk.Common.Tests`, `tests/Sdk.Common.PipelineParityTests`), not of this repository, and the paths below are
> those of the time it was written here. It stays until it moves with that repository's own agent docs.

**ETL Pipeline Pattern**
- `EtlDataOrchestrator` executes pipelines via `IPipelineNode` chains
- `IDataContext` is path-only on the routine node-author surface; backed by a single `DataContextImpl` over an `IReadSource` seam — `ElementSource` (zero-copy `JsonElement` read base) for the root, `LayeredSource` (aliases + parent-fallback + sparse `JsonNode` overlay) for iteration children; writes lift the overlay only on mutation. One generic struct-constrained JSONPath walker (`JsonPathWalker` over `IJsonView<TSelf>` with `ElementView`/`NodeView`, non-boxing) drives all reads over both the element base and the node overlay (it replaced the former dual `JsonPathEvaluator`/`JsonNodePath` read walkers — `JsonPathEvaluator` was deleted and `JsonNodePath`'s read path now delegates to the unified walker). `JsonNodePath` is retained for its write/normalize helpers (`Set`/`Remove`/`NormalizePathOrRelative`) plus thin public `Select`/`SelectAll` read wrappers (re-added for adapter call sites that hold a raw `JsonNode`); both wrappers route through `JsonPathWalker.Select` over `NodeView`. Iteration nodes (`ForEachNode`, `ObjectIteratorNode`, `SelectByPathNode`) use `Parallel.ForAsync` over alias-based zero-copy child sub-contexts. The full node-author surface is:
  - `Get<T>(path)` / `GetValue(path)` — typed scalar read / untyped `JsonElement` read
  - `TryGet<T>(path, out value)` — non-throwing typed read (returns false when path is absent/null)
  - `Set<T>(path, value, ...)` — typed write
  - `GetKind(path)` — inspect whether a path holds a value, null, array, object, or is undefined
  - `Length(path)` — element count for arrays
  - `Iterate*Async(path, body)` — iteration over arrays
  - `UpdateMatchesAsync(jsonPath, body)` — multi-match read/write (per-match sub-contexts)
  - `SelectMatches(jsonPath)` — read-only multi-match; returns `IEnumerable<IDataContext>` of detached sub-contexts, one per JSONPath match. Replaces the removed `EnumerateMatches` (which returned raw `JsonNode?` values and exposed STJ internals to callers).
  - Node code must not pass `JsonSerializerOptions` to any `IDataContext` method — all STJ details are internal to the context implementation.
- `SystemTextJsonOptions.Default` (`src/Sdk.Common/EtlDataPipeline/SystemTextJsonOptions.cs`) is the central STJ options bundle used internally — it **preserves** explicit nulls (`DefaultIgnoreCondition = Never`) so node code can distinguish `DataKind.Null` from `DataKind.Undefined`, and carries the CK/Rt converters. (The canonical `RtSystemTextJsonSerializer` it derives from uses `WhenWritingNull`; the pipeline bundle overrides that.) The `options` parameter that was formerly on some `IDataContext` methods has been removed; callers no longer control serialization options.
- Pipeline configuration via YAML/JSON with `NodeName` and `NodeConfiguration` attributes for type discovery
- Reverse-ordered node delegation (middleware-style)

**Pipeline Schema Generation**
- `AdapterBuilder` and `WebAdapterBuilder` support `--generate-pipeline-schema <output-path>` CLI parameter
- `NodeSchemaRegistry` discovers all registered pipeline nodes and generates a JSON Schema (`pipeline-schema.json`) describing available node configurations
- All enum values in the schema use CONSTANT_CASE format (e.g. `NOT_EQUALS`, `DATE_TIME`)
- MSBuild target `GeneratePipelineSchema` in `Sdk.Plug.Simulation.csproj` runs after Build using `dotnet exec "$(TargetPath)"` to auto-generate the schema
- Incremental: only regenerates when the binary changes
- Opt-out: set MSBuild property `GeneratePipelineSchema=false`

## Testing

- **Pipeline parity suite** (`tests/Sdk.Common.PipelineParityTests/`): the authoritative Newtonsoft-oracle contract for STJ pipeline behaviour — read/write JSONPath parity, read-after-`Set`/multi-match operation parity (`OperationParityTests`), the overlay write-then-read predicate matrix (`OverlayWriteThenReadParityTests`), encoding byte-parity (`EncodingParityTests`, `UnsafeRelaxedJsonEscaping`), and attribute-value CLR-type round-trip parity. Newtonsoft defines correctness; documented irreducible divergences live in `AttributeValueParityCorpus`.
- **Allocation/memory gates** (`tests/Sdk.Common.Tests/EtlDataPipeline/`): `ForEachMemoryBenchmark`, `DataContextBigDocReadAllocationGate`, `TypedGetAllocationGate`, and `DataContextChildSelectMatchesTests` read process-wide GC counters, so they are isolated in the `[CollectionDefinition("AllocationGates", DisableParallelization = true)]` collection (`AllocationGatesCollection.cs`) with frozen absolute ceilings — relative ratios flaked under xUnit parallel execution.
