# Changelog

Notable changes of the OctoMesh communication SDK. Versions are set by the release pipeline; entries
collect under `Unreleased` until the next communication release train.

## [Unreleased]

### Added

- `Logger@1` logs values from the data context (AB#9335, Feature AB#9249). New optional properties:
  `messagePath` (JSONPath to the message; exactly one of `message` and `messagePath` is required),
  `level` (`Debug`, `Information`, `Warning`, `Error`; default `Information`), `attributes` (list of
  `name` plus `value` or `valuePath`, at most 20, emitted as structured log properties) and
  `missingValue` (text for a missing or null value, default `<missing>`). Missing data never fails
  the node; objects and arrays are logged as compact JSON; the message is limited to 2048 and every
  attribute value to 256 characters, ending in `...[truncated]` when cut; values registered as secret
  are masked. Every Logger@1 line also carries the attributes `TenantId`, `PipelineRtId` and
  `NodePath`. The node schema publishes the new properties.
- `IPipelineLogger.Log(LogLevel, nodeId, nodePath, message, attributes)` and
  `INodeContext.Log(LogLevel, message, attributes)`: log a message as a value with structured
  attributes. Both are default interface members, so existing implementations keep compiling.

### Changed

- `Logger@1`: `message` is no longer required (see `messagePath`). Existing definitions keep their
  behaviour: same body `<nodePath>: <message>`, level Information. The message is now passed as a
  value instead of a format template; braces such as `{0}` were already kept verbatim by NLog when
  no arguments were given, and now no longer depend on that.
