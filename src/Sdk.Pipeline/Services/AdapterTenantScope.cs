namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     The dedicated-adapter implementation of <see cref="IAdapterTenantScope" /> (AB#4924,
///     increment 3).
/// </summary>
/// <remarks>
///     <para>
///         Registered as a <b>singleton</b> that carries an <see cref="AsyncLocal{T}" /> value, not
///         as a scoped service. The distinction matters: pipeline executions run concurrently on the
///         same process and a DI scope does not follow an async call chain, whereas the tenant of an
///         execution must. An <c>AsyncLocal</c> flows into every continuation of the execution that
///         set it and into nothing else, which is exactly the shape of "the tenant of this work
///         item".
///     </para>
///     <para>
///         🔴 <see cref="IsPoolMember" /> is hard-coded <c>false</c> and is deliberately not bound to
///         configuration. Increment 3 ships as a behaviour-preserving refactor that the whole fleet
///         bakes for one release before any lease exists; a flag that could flip it by environment
///         variable would defeat the point of baking it.
///     </para>
/// </remarks>
public sealed class AdapterTenantScope : IAdapterTenantScope
{
    // An INSTANCE field, not static: a static one would share the ambient tenant across
    // every AdapterTenantScope in the process, which is invisible in production (there is
    // one registration) but couples otherwise independent tests to each other and would
    // quietly couple two adapter hosts sharing a process.
    private readonly AsyncLocal<string?> _currentTenantId = new();

    /// <inheritdoc />
    public bool IsPoolMember => false;

    /// <inheritdoc />
    public bool HasTenant => !string.IsNullOrWhiteSpace(_currentTenantId.Value);

    /// <inheritdoc />
    public string TenantId =>
        _currentTenantId.Value is { } tenantId && !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "No tenant is in scope. IAdapterTenantScope.TenantId may only be read inside a " +
                "pipeline execution (EtlDataOrchestrator enters the scope) or inside an explicit " +
                "BeginExecution. Reading it outside one is a bug: there is no correct process-wide " +
                "answer, which is precisely why AdapterOptions.TenantId was removed.");

    /// <inheritdoc />
    public IDisposable BeginExecution(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var previous = _currentTenantId.Value;
        _currentTenantId.Value = tenantId;
        return new Restore(this, previous);
    }

    /// <summary>
    ///     Restores the enclosing tenant (or none) when the execution leaves the scope.
    /// </summary>
    /// <remarks>
    ///     Restores rather than clears so that a node starting a sub-pipeline does not strip the
    ///     tenant off the outer execution when the sub-pipeline finishes. Idempotent — the
    ///     orchestrator's scope can be disposed on both the success and the exception path.
    /// </remarks>
    private sealed class Restore(AdapterTenantScope owner, string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._currentTenantId.Value = previous;
        }
    }
}
