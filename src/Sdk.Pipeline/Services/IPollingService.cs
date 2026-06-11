namespace Meshmakers.Octo.Sdk.Common.Services;

/// <summary>
///     Interface for the polling service that allows to add callbacks that get invoked at a specified interval
/// </summary>
public interface IPollingService
{
    /// <summary>
    ///     Adds a callback to be invoked at the specified interval
    /// </summary>
    /// <param name="interval">The interval the callback gets called.</param>
    /// <param name="callback">The function of the callback</param>
    PollingHandle RegisterCallback(TimeSpan interval, Func<Task> callback);
    
    /// <summary>
    /// Clears all callbacks
    /// </summary>
    /// <returns></returns>
    void ClearCallbacks();

    /// <summary>
    ///     Unregisters a callback
    /// </summary>
    void UnregisterCallback(PollingHandle handle);
}