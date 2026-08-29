namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The authenticated caller of a pipeline trigger, verified by the trigger's authorization
///     (e.g. FromHttpRequest@2 bearer validation — AB#4975). Carried through
///     <see cref="ExecutePipelineOptions" /> onto the ETL context so entity-writing nodes can act
///     under the caller's identity (RtCreatedBy stamping, data-level permissions). Deliberately a
///     slim value object without token material: the pipeline data root is echoed in HTTP responses
///     and persistable, so no credential may ever travel with it.
/// </summary>
/// <param name="SubjectId">Subject id ("sub" claim) or null for client-credentials tokens</param>
/// <param name="TenantId">Tenant id claim of the caller, if present</param>
/// <param name="Email">E-mail claim of the caller, if present</param>
/// <param name="Name">Display name claim of the caller, if present</param>
/// <param name="Roles">Role claims of the caller</param>
public sealed record VerifiedPrincipal(
    string? SubjectId,
    string? TenantId,
    string? Email,
    string? Name,
    IReadOnlyList<string> Roles);
