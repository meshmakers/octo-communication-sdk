# Secret values in the pipeline

AB#5538, concept AB#5528 (`octo-construction-kit-engine/docs/concept-secret-attribute-type.md`).


CK attributes of value type `Secret` reach the data context only as the marker `{"isSet": true|false}`
— the engine serialisers (`RtSecretValueWireFormat`) never write the plaintext or the envelope. Two
things in this repo build on that:

- **`PipelineSecretValues`** (`EtlDataPipeline/PipelineSecretValues.cs`): `IsSecretMarker` (an object
  whose only property is a boolean `isSet`) and the guards the type-switch nodes call —
  `ConvertDataType@1`, `SetPrimitiveValue@1`, `If@1`, `Switch@1`, `ExecuteCSharp@1` refuse a configured
  value type `Secret` and a marker at a path they read with
  `PipelineExecutionException.SecretNotSupported` ("Secret not supported"). Reading `…password.isSet`
  as Boolean stays allowed; that is how a pipeline asks whether a secret is configured.
- **`PipelineSecretRegistry`** — the plaintexts one execution must keep out of its diagnostics. The
  root `NodeContext` creates it, children share it (`INodeContext.SecretRegistry` /
  `RegisterSecret`, default interface members so foreign `INodeContext` implementations keep
  compiling), and the root hands it to the debugger (`IPipelineDebugger.AddSecretRegistry`, default
  no-op). Masked with `***`: debug snapshots and dry-run intents (`DefaultPipelineDebugger`, fail
  closed when redaction throws), every message and argument logged through `NodeContext`, and the
  `SetPipelineExecutionResult@1` output. Redaction is **by value**, so a copy, a concatenation
  (`Bearer …`) or a loop child is masked too; values shorter than 4 characters only as whole strings.
  The data itself is never changed — downstream nodes still read the plaintext.
- **Node errors** (AB#5538 review): `PipelineSecretRegistry.RedactException` returns the exception
  unchanged unless a registered value appears in its chain, otherwise a masked copy without the
  original (a `DataPipelineException` stays one). `EtlDataOrchestrator` applies it to every node error
  before wrapping or rethrowing, and `NodeContext.Error(Exception, …)` before logging, so neither the
  execution log nor the error message `AdapterTriggerContext` reports with
  `ReportExecutionEndAsync(Failed, ex.Message)` (persisted on the pipeline execution) carries a
  plaintext quoted by a node — e.g. a conversion error on a revealed password.

🔴 **A node that puts a plaintext secret into the data context or resolves a credential from
configuration must call `nodeContext.RegisterSecret(value)`.** `RevealSecret@1` (mesh adapter) is the
canonical writer. A value written but not registered is visible in the Studio debug panel. Tests:
`Sdk.Common.Tests/EtlDataPipeline/Secrets/*`.
- **Configurations copied into the data context** (AB#5538 review): the controller ships configuration
  entities with Secret values revealed as plain strings. `GetPipelineConfigByWellKnownName@1` (and the
  mesh adapter's `GetPipelineConfigByCkTypeId@1`) call `ConfigurationSecrets.Register` before writing:
  the CK type (`IGlobalConfiguration.GetConfigurationTypeId`, default member) is resolved to its Secret
  attribute names by `IConfigurationSecretAttributeResolver` (default `NoConfigurationSecretAttributeResolver`
  → `null`; the mesh adapter uses the CK cache), and every string value under such a property name, at
  any depth, is registered. An unresolvable type falls back to
  `ConfigurationSecrets.KnownCredentialAttributeNames` (System.Communication 3.40 credential names) and
  logs a warning without values. Tests: `ConfigurationSecretsTests`.
