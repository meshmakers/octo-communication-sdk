using Meshmakers.Common.Shared;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

internal class GlobalConfiguration(IEnumerable<ConfigurationDto> configurationDtos) : IGlobalConfiguration
{
    private readonly Dictionary<string, ConfigurationDto> _configurationDictionary =
        configurationDtos.ToDictionary(k => k.ConfigurationName.ToLower(), v => v);

    public IEnumerable<string> GetNames()
    {
        return _configurationDictionary.Keys;
    }

    public bool IsDefined(string configurationName)
    {
        return _configurationDictionary.ContainsKey(configurationName.ToLower());
    }

    public T GetValue<T>(string configurationName) where T : class
    {
        if (_configurationDictionary.TryGetValue(configurationName.ToLower(), out var configurationDto))
        {
            return configurationDto.ConfigurationValue.Deserialize<T>();
        }

        throw PipelineExecutionException.GlobalConfigurationParameterNotFound(configurationName);
    }

    public string GetRawJson(string configurationName)
    {
        if (_configurationDictionary.TryGetValue(configurationName.ToLower(), out var configurationDto))
        {
            return configurationDto.ConfigurationValue;
        }

        throw PipelineExecutionException.GlobalConfigurationParameterNotFound(configurationName);
    }

    public IEnumerable<string> GetAllRawJsonByCkTypeId(string ckTypeId)
    {
        return _configurationDictionary.Values
            .Where(c => c.ConfigurationTypeId.SemanticVersionedFullName.Equals(ckTypeId, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ConfigurationValue);
    }
}