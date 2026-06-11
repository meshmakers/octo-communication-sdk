namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
/// Handle to a polling registration
/// </summary>
public class PollingHandle(IPollingService pollingService) : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        pollingService.UnregisterCallback(this);
    }
}