namespace Meshmakers.Octo.Sdk.SimulationNodes.Services;

/// <summary>
/// Interface of simulation service to generate values for simulation.
/// </summary>
public interface ISimulationService
{
    /// <summary>
    /// Generate a value using the specified generator.
    /// </summary>
    /// <param name="locale">Locale.</param>
    /// <returns>Generated value.</returns>
    ISimulator GetSimulator(string locale);
}