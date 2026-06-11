using System.Collections.Concurrent;
using Bogus;

namespace Meshmakers.Octo.Sdk.SimulationNodes.Services;

/// <summary>
/// Generates values for simulation.
/// </summary>
public class SimulationService : ISimulationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, Faker> _fakers;

    /// <summary>
    /// Constructor.
    /// </summary>
    public SimulationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _fakers = new ConcurrentDictionary<string, Faker>();
    }


    /// <inheritdoc />
    public ISimulator GetSimulator(string locale)
    {
        return new Simulator(locale, _serviceProvider);
    }
}