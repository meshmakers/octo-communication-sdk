namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Configuration of a process that runs as an adapter pool member (AB#4924, increment 6).
/// </summary>
/// <remarks>
///     <para>
///         Section <c>AdapterPool</c>, i.e. <c>OCTO_ADAPTERPOOL__POOLTENANTID</c> and friends. The
///         values name the <b>lending</b> tenant and its pool; they never name a borrower, because a
///         member has no borrower until a lease arrives.
///     </para>
///     <para>
///         🔴 <b>There is no <c>DedicatedTenantId</c> here, and there must not be.</b> A pool member
///         that carried one would have a process-wide tenant to fall back on, which is the exact
///         hazard <c>AdapterOptions.TenantId</c> was deleted to remove (increment 3). Between leases
///         a member serves nobody.
///     </para>
/// </remarks>
public class AdapterPoolMemberOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AdapterPool";

    /// <summary>
    ///     The tenant that owns the pool — the lender. Authorization of the management connection is
    ///     evaluated against it (concept §8, Q4).
    /// </summary>
    public string? PoolTenantId { get; set; }

    /// <summary>RtId of the <c>AdapterPool</c> entity in <see cref="PoolTenantId" />.</summary>
    public string? PoolRtId { get; set; }

    /// <summary>
    ///     Stable identity of this member process across reconnects, recorded on every execution it
    ///     serves as <c>LeasedOnMemberId</c>. Defaults to the machine name, which is the pod name in
    ///     Kubernetes — the value an operator would look for anyway.
    /// </summary>
    public string? MemberId { get; set; }

    /// <summary>
    ///     Whether this process is configured as a pool member at all. Both identifiers are required:
    ///     a member that knew its pool but not its tenant could not be authorized, and one that knew
    ///     its tenant but not its pool could not be routed a lease.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(PoolTenantId) && !string.IsNullOrWhiteSpace(PoolRtId);

    /// <summary>The member id to present, falling back to the machine name.</summary>
    public string EffectiveMemberId =>
        string.IsNullOrWhiteSpace(MemberId) ? Environment.MachineName : MemberId;
}
