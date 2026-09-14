namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
///     Declares the scheduling class a trigger node implies on a leased adapter pool (AB#4924).
/// </summary>
/// <remarks>
///     <para>
///         Goes on the node <b>configuration</b> record, like
///         <see cref="NodeRequiresRunningProcessAttribute" /> and <see cref="NodeDeprecatedAttribute" />.
///         The reflection-based descriptor scan picks it up, so a future or third-party trigger node
///         self-describes and no controller-side list has to learn about it.
///     </para>
///     <para>
///         Parameterized rather than a marker, because this is not a boolean: it follows
///         <see cref="NodeDeprecatedAttribute" />, not <see cref="NodeRequiresRunningProcessAttribute" />.
///     </para>
///     <para>
///         Only meaningful on a <b>trigger</b>: it is the trigger that says how the work arrived,
///         and therefore whether anybody is waiting for it. The descriptor scan records it for any
///         node, but the controller only reads it from triggers.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class NodeExecutionClassAttribute(PipelineExecutionClass executionClass) : Attribute
{
    /// <summary>
    ///     The class this trigger implies.
    /// </summary>
    public PipelineExecutionClass ExecutionClass { get; } = executionClass;
}
