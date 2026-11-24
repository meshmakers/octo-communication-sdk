---
description: Caller identity carried by ExecutePipelineOptions and why it ends at a pipeline chain.
applies_to: src/Sdk.Pipeline/Services/{ExecutePipelineOptions,VerifiedPrincipal}.cs, src/Sdk.Pipeline/EtlDataPipeline/Nodes/{Triggers/From*,Loads/ToPipelineDataEvent}Node.cs
---

# Execution identity — what this repo owns

The adapter decides which identity a pipeline execution acts as (`PipelineIdentityResolver`,
in `octo-mesh-adapter`). What lives **here** is the carrier and the triggers that fill it:

- **`ExecutePipelineOptions.VerifiedPrincipal`** — the caller a trigger authenticated. A
  slim, token-free value object, because the trigger projects it into the pipeline data root, which is
  echoed in HTTP responses, persistable by `SetPipelineExecutionResult@1` and shown in the Studio
  debug panel. Carries `PreferredChannel` (optional last positional parameter so every
  pre-existing constructor call keeps compiling): the user's preferred outbound channel for
  system-initiated messages, canonical uppercase `"TEAMS"` | `"SIGNAL"` (extensible), filled by the
  mesh adapter's verified-caller directory from the identity record and propagated verbatim — null
  for callers resolved from bearer claims (no such claim exists).
- **`ExecutePipelineOptions.CallerAccessToken`** — the caller's **raw** bearer token, for
  nodes that must act as the caller against another service (the delegation / on-behalf-of grant needs
  it as `subject_token`). 🔴 It must never reach the data root, never `VerifiedPrincipal`, and never
  `IEtlContext.Properties` — that dictionary hangs on the `PipelineRegistration` and is shared across
  **all runs** of the pipeline, so a token left there would outlive its request. It is a
  **per-execution** side channel: trigger → ETL context of exactly this execution, nowhere else.
- **`ExecutePipelineOptions.MethodInvocation`** — nullable `ExecutePipelineMethodInvocation`
  marker (octo-sdk) that the run was started by a CK method invocation; consumed by the mesh adapter's
  generic-create guard. `FromExecutePipelineCommandNode` copies `request.MethodInvocation` **unconditionally**
  — for every `CallerBindingMode` and caller outcome, deliberately *not* via
  `ExecuteCommandCallerCarryThrough.Apply` (which only writes on `UseResolvedCaller`). Transport-only side
  channel like `CallerAccessToken`: never in the data root or `IEtlContext.Properties`. Null when the request
  has none. Set only by the controller's method invoker; the marker does not travel over `ToPipelineDataEvent`.
- Both surface on `IEtlContext` as **default interface members**, so an adapter implementing the
  interface itself was not broken by their addition.

## The identity ends at a pipeline chain — by decision, and visibly

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
and that neither the token nor the subject reaches the message. The trigger-kind matrix that shows how this row fits the other trigger kinds
lives in the `octo-mesh-adapter` repository.
