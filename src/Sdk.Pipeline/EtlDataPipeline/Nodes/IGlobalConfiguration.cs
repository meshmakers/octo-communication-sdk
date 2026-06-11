namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

/// <summary>
/// Global configuration access for the pipeline
/// </summary>
public interface IGlobalConfiguration
{
    /// <summary>
    /// Returns a list of names that are available in the configuration
    /// </summary>
    /// <returns></returns>
    IEnumerable<string> GetNames();
    
    /// <summary>
    /// Is the parameter defined in the configuration
    /// </summary>
    /// <param name="configurationName">Name of the configuration parameter</param>
    /// <returns></returns>
    bool IsDefined(string configurationName);

    /// <summary>
    /// Returns the value of the parameter as a deserialized object
    /// </summary>
    /// <param name="configurationName">Name of the configuration parameter</param>
    /// <typeparam name="T">Type of the object to return</typeparam>
    /// <returns>Value of the parameter</returns>
    T GetValue<T>(string configurationName) where T : class;

    /// <summary>
    /// Returns the raw JSON string value of the parameter
    /// </summary>
    /// <param name="configurationName">Name of the configuration parameter</param>
    /// <returns>Raw JSON string value of the parameter</returns>
    string GetRawJson(string configurationName);

    /// <summary>
    /// Returns the raw JSON string values of all configurations matching the given CkTypeId
    /// </summary>
    /// <param name="ckTypeId">The semantic versioned full name of the CkTypeId to search for</param>
    /// <returns>Raw JSON string values of all matching configurations</returns>
    IEnumerable<string> GetAllRawJsonByCkTypeId(string ckTypeId);
}