namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;

/// <summary>
///     The scheduling class a trigger node implies when its pipeline runs on a leased adapter pool
///     (AB#4924).
/// </summary>
/// <remarks>
///     <para>
///         Two classes, never a numeric priority. A number needs somebody to assign it and makes
///         "why is my job not running" unanswerable in a queue view; two classes stay explainable —
///         <i>your interactive job runs before your batch jobs, and tenants take turns</i>.
///     </para>
///     <para>
///         🔴 The class orders work only <b>within one tenant's turn</b> in the round-robin
///         rotation. It never reorders tenants against each other, so it cannot reintroduce the
///         starvation that round-robin exists to prevent.
///     </para>
///     <para>
///         The numeric values are the keys of the <c>PipelineExecutionClass</c> CK enum in
///         <c>System.Communication</c> and must stay in lockstep with it — they are persisted on
///         every pipeline entity and travel on the descriptor wire contract. They are also ordered
///         so that ascending value equals scheduling order, so a scheduler that sorts by the raw
///         value gets the right answer rather than the reverse.
///     </para>
/// </remarks>
public enum PipelineExecutionClass
{
    /// <summary>
    ///     Work a human is waiting for: an HTTP request, a manual run, a Studio "execute now".
    /// </summary>
    Interactive = 0,

    /// <summary>
    ///     Everything scheduled or event-driven. The default for any trigger that declares nothing,
    ///     because a trigger nobody has classified must never jump a queue.
    /// </summary>
    Batch = 1
}
