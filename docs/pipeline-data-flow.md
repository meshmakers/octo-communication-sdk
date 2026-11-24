---
description: "How pipeline data is read and written: IDataContext, STJ only, integer coercion, multi-match path settings."
applies_to: src/Sdk.Pipeline/EtlDataPipeline/**, tests/Sdk.Common.PipelineParityTests/**
---

# Pipeline data flow

The pipeline data path is **System.Text.Json only** and goes through the path-only `IDataContext`
(`EtlDataPipeline/IDataContext.cs`). Nodes never see `JToken`/`JObject`/`JArray`, and node code must
not pass `JsonSerializerOptions` into any `IDataContext` method — the STJ details are internal to the
implementation. `SystemTextJsonOptions.Default` is the single options bundle; its
`UnsafeRelaxedJsonEscaping` encoder is load-bearing for every consumer that **hashes** serialized
output.

## Integer coercion on typed reads

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

## What a node's `…Path` setting may point at

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
`JsonPathWalker.Select(...).FirstOrDefault()`, so before multi-match support a wildcard path silently returned
**only the first match** while the overlay was still clean and `null` once any node had written to
the document — the same pipeline, two wrong answers, depending on what ran before it. `GetArray<T>`
therefore classifies the path first (`JsonPathShape.IsMultiMatch`) and collects multi-match paths
through `SelectMatches`. The classifier's pre-filter is a plain character test, so an ordinary
property/index path never pays a parse and the single-value fast paths stay allocation-identical —
which `TypedGetAllocationGate` and `DataContextBigDocReadAllocationGate` pin. **`Get<T>` is
unchanged and still resolves to the first match**; only `GetArray<T>` is multi-match aware.

⚠️ **Inside `ForEach@1` a multi-match path sees the child's own document only** — its writes
(`$.key…`) and its aliases (`$.full…`), never the parent-fallback chain, because folding the parent
in would re-materialise the whole outer document per call (the allocation alias pruning removed). So reach the outer document through `$.full[*]…`, not through a bare outer path: the
single-value read of that bare path resolves through the parent, the multi-match read returns
`null`. That boundary is older than multi-match support (it is `SelectMatches`/`UpdateMatchesAsync` semantics)
and is pinned by `DataContextGetArrayMultiMatchTests`.
