namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Tracks whether this adapter process is currently <b>registered</b> at the communication
///     controller's adapter hub.
/// </summary>
/// <remarks>
///     A live <see cref="Meshmakers.Octo.Sdk.ServiceClient.ISignalRClient{TOptions}.IsAlive" /> is
///     <i>not</i> the same thing: the SignalR connection can be up while the controller has no
///     registration for this adapter, because registration is a hub invoke that can fail on its own
///     (AB#5409 — five prod-1 adapters sat connected-or-reconnecting but unregistered for eleven
///     hours; everything the controller has to push — configuration updates, <c>DeployDataFlow</c>,
///     HTTP-activator calls — silently went nowhere, while the pods stayed <c>1/1 Running</c> and
///     both health endpoints answered 200).
///     Registration is therefore the only signal that means "the controller can reach me", and it is
///     what the readiness probe and the recovery watchdog are built on.
/// </remarks>
public interface IAdapterHubRegistrationState
{
    /// <summary>
    ///     UTC time this process started. Anchors the readiness grace period.
    /// </summary>
    DateTime ProcessStartUtc { get; }

    /// <summary>
    ///     True while the last registration attempt succeeded and nothing has invalidated it since.
    /// </summary>
    bool IsRegistered { get; }

    /// <summary>
    ///     True once the adapter registered successfully at least once in this process. The recovery
    ///     watchdog only arms after that: an adapter that has <i>never</i> registered may be waiting
    ///     for a tenant that is not enabled yet, and restarting it in a loop would fix nothing.
    /// </summary>
    bool HasEverRegistered { get; }

    /// <summary>
    ///     UTC time of the last successful registration, or <c>null</c> if there was none.
    /// </summary>
    DateTime? LastRegisteredUtc { get; }

    /// <summary>
    ///     Message of the last failed registration attempt, or <c>null</c>. Surfaced in the health
    ///     check output so the reason is visible without digging through a rotated log.
    /// </summary>
    string? LastFailureMessage { get; }

    /// <summary>
    ///     Records a successful registration at the adapter hub.
    /// </summary>
    void MarkRegistered();

    /// <summary>
    ///     Records that this adapter is not (or no longer) registered.
    /// </summary>
    /// <param name="reason">Human readable reason, kept for the health check output.</param>
    void MarkNotRegistered(string reason);
}

/// <inheritdoc />
public sealed class AdapterHubRegistrationState : IAdapterHubRegistrationState
{
    private readonly object _gate = new();
    private DateTime? _lastRegisteredUtc;
    private string? _lastFailureMessage;
    private volatile bool _isRegistered;
    private volatile bool _hasEverRegistered;

    /// <inheritdoc />
    public DateTime ProcessStartUtc { get; } = DateTime.UtcNow;

    /// <inheritdoc />
    public bool IsRegistered => _isRegistered;

    /// <inheritdoc />
    public bool HasEverRegistered => _hasEverRegistered;

    /// <inheritdoc />
    public DateTime? LastRegisteredUtc
    {
        get
        {
            lock (_gate)
            {
                return _lastRegisteredUtc;
            }
        }
    }

    /// <inheritdoc />
    public string? LastFailureMessage
    {
        get
        {
            lock (_gate)
            {
                return _lastFailureMessage;
            }
        }
    }

    /// <inheritdoc />
    public void MarkRegistered()
    {
        lock (_gate)
        {
            _lastRegisteredUtc = DateTime.UtcNow;
            _lastFailureMessage = null;
        }

        _isRegistered = true;
        _hasEverRegistered = true;
    }

    /// <inheritdoc />
    public void MarkNotRegistered(string reason)
    {
        lock (_gate)
        {
            _lastFailureMessage = reason;
        }

        _isRegistered = false;
    }
}
