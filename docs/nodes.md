---
description: Built-in node inventory and the behaviour of DateTime, ExecuteCSharp and Logger nodes.
applies_to: src/Sdk.Pipeline/EtlDataPipeline/Nodes/**, src/Sdk.Pipeline/EtlDataPipeline/DefaultPipelineLogger.cs
---

# Node inventory (`src/Sdk.Pipeline/EtlDataPipeline/Nodes/`)

- **Triggers**: `FromPipelineDataEvent@1`, `FromExecutePipelineCommand@1`, `FromPolling@1`
- **Control**: `ForEach`, `For`, `Group`, `If`, `Switch`, `ObjectIterator`, `SelectByPath`
- **Extracts**: `SetJson`, `SetPrimitiveValue`, `SetArrayOfPrimitiveValues`,
  `GetPipelineConfigByWellKnownName`
- **Transforms**: `Concat`, `FormatString`, `Map`, `Project`, `Flatten`, `Join`, `Distinct`, `GroupBy` (not the editor container `Group`), `Math`,
  `LinearScaler`, `Hash`, `Base64Encode`/`Decode`, `ConvertDataType`, `DateTime`, `TransformString`,
  `ExecuteCSharp`, `Logger`, `PrintDebug`, `SumAggregation`
- **Loads**: `ToPipelineDataEvent@1`, `ToWebhook@1`, `SetPipelineExecutionResult@1`
- **Buffering**: `Buffer`, `BufferRetrieval`

Domain nodes (entity CRUD, stream data, HTTP, files, PDF, mail, …) live in `octo-mesh-adapter`, not
here.

## `DateTime@1` time zone operations

Operation ordinals are persisted in deployed pipelines and the generated schema maps names onto them,
so new operations are **only appended** (`DateTimeOperationDto`; `DateTimeOperationDto_AppendsNewOperations…`
pins every ordinal). Still `DateTime@1`: no node version, no CK change.

- `ConvertToTimeZone` (11): UTC instant → **wall clock** of an IANA zone, result Kind `Unspecified`
  (serializes without `Z`). A source without Kind is read as UTC.
- `ConvertFromTimeZone` (13): wall clock → **UTC instant**, result Kind `Utc`. The zone comes from
  `valuePath` or `value`; unknown/empty zone fails the node.
- `StartOfMonth` (14): first of the month, 00:00, **keeps the Kind** of the source, no zone conversion.

🔴 `ConvertFromTimeZone` takes the **clock fields as written in the value** as local time: `Z`, a UTC
offset (`+05:00`) and no marker at all are all ignored, so the result never depends on the machine's
zone. `GetSourceWallClock` starts with the same strict typed `Get<DateTime?>` as the other operations
(only ISO 8601 date-times are valid; `"12:00:00"` or `"07/15/2026"` fail instead of being completed with
today's date). Only a value that comes back as Kind `Local` — STJ converts a value with an offset to the
**host's** local time, which would shift the clock fields by a host-dependent amount — is parsed again
from its text (`DateTimeOffset.TryParse`, invariant, `AssumeUniversal`). The error message formats the
value with the invariant culture and keeps fractional seconds. Daylight saving time is fixed by contract: a non-existing local time (spring gap) **fails the
node** (`PipelineExecutionException.LocalTimeDoesNotExist`, names value and zone); an ambiguous local
time (autumn repeat) maps to the **earlier** instant (daylight saving offset = the larger one of
`GetAmbiguousTimeOffsets`). `TimeZoneInfo.ConvertTimeToUtc` is deliberately not used: for an ambiguous
time it picks the standard-time offset, i.e. the later instant. The only round trip that cannot hold is
the second pass of the repeated hour (its wall clock equals the first pass). A lenient mode for the gap
(shift forward) would be a separate story, not an option.

## ExecuteCSharp argument typing

`ExecuteCSharp@1` arguments are declared in the generated script with the real C# type for their
`DataType`: the scalars map to `string`/`int`/`long`/`bool`/`double`/`DateTime`, and the **array**
kinds map to typed CLR arrays — `StringArray → string[]`, `IntArray`/`IntegerArray` → `int[]`,
`RecordArray → object[]` (elements stay `JsonElement` for complex records). Those three are ALL the
array kinds `AttributeValueTypesDto` defines; everything else falls back to `object`. Incoming values
are materialized into those arrays regardless of shape (typed array, `JsonElement`/`JsonNode` array,
or a native list from a fixed configuration `Value`), and array `ReturnType`s materialize `T[]`,
`List<T>` and lazy LINQ enumerables alike. Previously array arguments were declared `object` and
arrived as a boxed `JsonElement`, so `foreach` over them failed to compile — pipeline scripts written
against that era cast the argument via `((JsonElement)arg).EnumerateArray()`; such scripts must be
made version-tolerant while pre-fix adapter images are still deployed: assign the argument to an
`object` first (`object value = arg;`), then test `value is JsonElement` and fall back to the typed array.
On a new image `arg` has a static array type, so neither a direct `(JsonElement)arg` cast nor an
`arg is JsonElement` pattern compiles; going through `object` compiles on both image generations. Null/absent arguments still coalesce to the type's default
(`null` for arrays).

### A JSON type that does not match the declared `dataType`

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
there would change behaviour far outside this node. And **`DateTime` stays strict**: STJ reads only
its ISO 8601-1:2019 *extended* profile (e.g. `2026-07-15T12:00:00Z`; the basic form `20260715T120000Z` is
rejected, so pipeline authors must not rely on it), and whatever it rejects (`"01.02.2026"`, a bare Unix number) is
ambiguous; `DateTime.Parse` under the invariant culture would guess a month/day order rather than
fail. Note that numeric strings into `Double`/`Int` already parsed before that fallback —
`SystemTextJsonOptions.Default` inherits `NumberHandling.AllowReadingFromString` from the CK
engine's `RtSystemTextJsonSerializer.CreateDefault()`, and the parity converters honour it.

### Missing argument paths: `optional`, `defaultValue` and one warning per execution

A missing `valuePath` used to log one WARNING **per record**; inside a `ForEach@1` over optional
fields that was ~808k warnings a day in production.
`ScriptArgument` now has two additive fields, and existing YAML runs unchanged:

| Field | Effect |
|---|---|
| `optional: true` | A missing path is silent (per-record line at Debug only). |
| `defaultValue` | Passed when the path is missing **or** holds null (instead of null, which the script sees as its declared type's default: null, 0, false, `DateTime.MinValue`). Converted to `dataType` on **every** execution, also when the path resolves, so an inconvertible default fails the first run with a message naming argument, type and value — not the first record that misses it. YAML hands a scalar default over as a string, the JSON pipeline serializer as a boxed `JsonElement`; scalars are unwrapped before the conversion (`JsonElement` is not `IConvertible`). |

A missing path of a **non-optional** argument is counted in the execution's
`PipelineLogAggregator` (`INodeContext.LogAggregator`, one instance per execution, inherited by child
node contexts like `SecretRegistry`) and reported **once per execution and argument** as a warning
with the count (`… was not found in {MissingCount} record(s) of this execution …`).
`EtlDataOrchestrator` flushes it in its `finally` block — on success and failure, before the debugger
ends — so the line also reaches the Studio debug panel. Entries are keyed by the node **configuration
object** (same instance for every iteration; the node path carries the iteration index) plus the
argument name. A path that exists with value null stays silent, as before.

`optional` is `bool?` on purpose (YamlDotNet writes null for `optional:` without a value, and a
non-nullable value type would also be marked required in the generated schema). The JSON schema
types `defaultValue` as any value; the type check against `dataType` is the runtime check above.
A node context without an aggregator (test doubles, custom `INodeContext`) warns per record as before.
Tests: `ExecuteCSharpNodeTests` (`*OptionalArgument*`, `*NonOptional*`, `*Default*`, `*NullValuedPath*`,
`*WithoutAggregator*`) and `ExecuteCSharpOptionalArgumentPipelineTests` (orchestrator, YAML, schema).

## `Logger@1` logs DataContext values with level and attributes

Still `Logger@1`, no node version: `message` (static) or `messagePath` (exactly one, else
`PipelineConfigurationException`, which `EtlDataOrchestrator` wraps in `DataPipelineException.NodeExecutionFailed`
like every node exception that is not already a `DataPipelineException`), `level` (`[DefaultValue(Information)]`, so YAML's `OmitDefaults` keeps `Debug`) (`Debug|Information|Warning|Error`, default Information),
`attributes` (`name` + `value`/`valuePath`, `valuePath` wins as in `SetPrimitiveValue@1`, at most 20,
unique names, reserved: `TenantId`, `PipelineRtId`, `NodePath` and the JSON log fields), `missingValue`
(default `<missing>`). Missing data never fails the node; non-strings render as compact JSON; message
≤ 2048, attribute values ≤ 256 characters including the `...[truncated]` marker; values go through
`SecretRegistry.Redact` before they are cut.

- **Body format unchanged by decision:** `<nodePath>: <message>` like every pipeline
  log line, so existing Logger@1 lines and Dash0 *contains* filters keep working. The node path is also
  an attribute. The limits and the secret masking apply to existing definitions too (a static message
  over 2048 characters is cut).
- **The message is a value, never a template.** `IPipelineLogger.Log(level, nodeId, nodePath, message,
  attributes)` and `INodeContext.Log(level, message, attributes)` are **default interface members**
  (implementers such as octo-mesh-adapter's test fakes keep compiling; the defaults forward the
  message without arguments and drop the attributes). `DefaultPipelineLogger.Log` logs a
  `PipelineLogRecordState` — a key/value list **without** `{OriginalFormat}` — with a formatter that
  returns the body, so NLog takes the body as it is and writes the attributes as event properties,
  i.e. top-level fields of the adapters' JSON log line that Dash0 promotes to attributes.
- `TenantId` and `PipelineRtId` come from `IEtlContext`, resolved lazily from the node context's
  service provider; when the pipeline runs with a context type for which the host did not call
  `RegisterEtlContext`, the line is written without them rather than failing the node.
- Values are redacted **before** they are cut, and objects/arrays as a tree before they are
  serialized, property names included (JSON escaping would hide a secret with a quote or backslash
  from text redaction); `NodeContext.Log` masks attribute names as well as values.
- The Studio debug log (`DebugPipelineLogger`) shows the message only, not the attributes.

Tests: `LoggerNodeTests` (node behaviour, limits, redaction, braces, invalid configuration),
`LoggerNodeNLogOutputTests` (JSON log line through NLog, pre-change baseline), and
`LoggerNodeConfigurationTests` (YAML/JSON, schema, FormatString@1 → Logger@1 pipeline run, default
interface members, Studio debug log).
