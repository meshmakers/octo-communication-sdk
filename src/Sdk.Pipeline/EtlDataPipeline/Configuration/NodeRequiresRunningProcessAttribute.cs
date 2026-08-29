namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
/// Marks a trigger-node configuration as process-bound: the trigger only fires while the
/// adapter process is running (in-process polling, in-memory event subscriptions). Scaling
/// such a workload to zero replicas would silently stop the trigger, so workloads with
/// pipelines using process-bound triggers are not on-demand capable (AB#4984).
/// The flag is injected into the generated JSON schema as the node-level extension
/// "x-requiresRunningProcess" (true) and travels to the communication controller via the
/// node descriptor registration.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NodeRequiresRunningProcessAttribute : Attribute;
