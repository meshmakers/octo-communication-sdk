using Microsoft.Extensions.Hosting;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Manages the lifetime of an adapter.
/// </summary>
public class AdapterLifetimeManagement
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    
    /// <summary>
    /// The current instance of the <see cref="AdapterLifetimeManagement"/>.
    /// </summary>
    internal static AdapterLifetimeManagement? Instance { get; private set; }

    /// <summary>
    /// Creates a new instance of the <see cref="AdapterLifetimeManagement"/> class.
    /// </summary>
    /// <param name="applicationLifetime"></param>
    public AdapterLifetimeManagement(IHostApplicationLifetime applicationLifetime)
    {
        _applicationLifetime = applicationLifetime;
        Instance = this;
    }
    


    /// <summary>
    /// Requests the adapter to stop.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public void Stop()
    {
        _applicationLifetime.StopApplication();
    }
}