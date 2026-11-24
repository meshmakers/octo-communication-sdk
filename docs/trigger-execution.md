---
description: Trigger execution class and why trigger command consumers are exclusive by design.
applies_to: src/Sdk.Pipeline/EtlDataPipeline/Configuration/NodeExecutionClassAttribute.cs, src/Sdk.Pipeline/EtlDataPipeline/Nodes/Triggers/**
---

# Trigger execution class (AB#4924 increment 4)

A trigger node declares the scheduling class it implies when its pipeline runs on a leased adapter
pool. Two classes only — `Interactive` (key 0) and `Batch` (key 1) — never a numeric priority: a
number needs somebody to assign it and makes "why is my job not running" unanswerable in a queue
view.

```csharp
[NodeName("FromHttpRequest", 2)]
[NodeExecutionClass(PipelineExecutionClass.Interactive)]
public record FromHttpRequestNodeConfiguration2 : TriggerNodeConfiguration { … }
```

Mechanics mirror `RequiresRunningProcess` exactly: the attribute goes on the node **configuration**
record, `NodeSchemaRegistry.BuildDescriptor` picks it up by reflection, and it travels on
`NodeDescriptor` / `NodeDescriptorDto.ExecutionClass`. Follow `NodeDeprecatedAttribute` rather than
`NodeRequiresRunningProcessAttribute` when adding more of these — it is a parameterized attribute,
not a marker.

- 🔴 **Append new descriptor fields last, with a default.** `NodeDescriptorDto` is the wire contract between an adapter and the controller; a trailing defaulted parameter is what lets an older adapter's payload still deserialize on a newer controller.
- The enum values are the keys of the `PipelineExecutionClass` CK enum in `System.Communication` and must stay in lockstep — they are persisted on every pipeline entity. They are ordered so ascending value equals scheduling order.
- Absent means `Batch`, the conservative answer: a trigger nobody classified must never jump a queue.
- The schema extension `x-executionClass` is emitted for **every trigger**, including the default — unlike `x-requiresRunningProcess`, which is emitted only when true. An editor showing the class only for nodes that opted in could not distinguish "classified Batch" from "not classified", which is the question the extension exists to answer. Non-triggers get nothing.
- Currently `Interactive`: `FromHttpRequest@1`, `FromHttpRequest@2` (both in `octo-mesh-adapter`), `FromExecutePipelineCommand@1`. Everything else relies on the default.

# Trigger command consumers are exclusive by design (AB#8105)

`FromExecutePipelineCommand@1` and the command half of `FromPipelineDataEvent@1` (AwaitResult callers) register their
command consumers with `CommandConsumerOptions.ExclusiveByDesign(reason)` (event hub, AB#8363) instead of the classic
two-argument `RegisterCommandConsumer`. The topology is **unchanged** — the classic exclusive queue under the same
address (`execute-pipeline-{tenant}-pipeline-{pipelineRtId}`, `pipelinedatacommand-{tenant}-dataflow-{id}-pipeline-{id}`),
no `::shared` queue, no bridge — so senders and the wire are untouched. What changes: registering a pipeline no longer logs
the AB#6909 deprecation warning, and the event hub logs the reason at Debug.

Why exclusive and not shared (decision Thor, 2026-11-06): the address belongs to **one pipeline**, the senders
(communication-controller `TriggerManagementService`, `ToPipelineDataEvent@1`) use the classic topology, and a shared
registration would need `MigratesLegacyCommand` — one legacy bridge per pipeline probing the broker every 2 s, for every
pipeline of every adapter. A second replica of a dedicated adapter is therefore a hot standby for these commands. Moving
them to shared belongs with a sender migration, not here. The fire-and-forget path of `FromPipelineDataEvent@1` is a durable
shared queue already (AB#5231) and is not affected. AB#8368 (one `LegacyCommandBridgeObserver` per process) does not apply:
these are dynamic registrations and exclusive-by-design creates no bridge.

The reasons are the constants `FromExecutePipelineCommandNode.ExclusiveByDesignReason` and
`FromPipelineDataEventNode.CommandExclusiveByDesignReason`; keep them in step with the audit table in
`octo-distributedEventHub/CLAUDE.md`. Tests: `PipelineTriggerCommandConsumerTopologyTests` — the options handed to
`IEventHubControl`, and both nodes against the event hub's real `IEventHubControl` (fake bus, captured log): no deprecation
warning, reason logged, classic queue name; a classic registration in the same harness does warn (control). Test doubles of
`IEventHubControl` must fake the three-argument overload (`A<CommandConsumerOptions>._`).
