using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.IntegrationTests.Fixtures;

/// <summary>
/// Test implementation of IGlobalConfiguration for integration tests.
/// </summary>
internal class TestGlobalConfiguration : IGlobalConfiguration
{
    private readonly Dictionary<string, object> _configuration = new();
    private readonly Dictionary<string, List<object>> _configurationByCkTypeId = new();

    public void SetValue<T>(string configurationName, T value) where T : class
    {
        _configuration[configurationName.ToLower()] = value;
    }

    public void AddValueByCkTypeId<T>(string ckTypeId, T value) where T : class
    {
        var key = ckTypeId.ToLower();
        if (!_configurationByCkTypeId.TryGetValue(key, out var list))
        {
            list = new List<object>();
            _configurationByCkTypeId[key] = list;
        }
        list.Add(value);
    }

    public IEnumerable<string> GetNames()
    {
        return _configuration.Keys;
    }

    public bool IsDefined(string configurationName)
    {
        return _configuration.ContainsKey(configurationName.ToLower());
    }

    public T GetValue<T>(string configurationName) where T : class
    {
        if (_configuration.TryGetValue(configurationName.ToLower(), out var value))
        {
            return (T)value;
        }

        throw new InvalidOperationException($"Configuration parameter '{configurationName}' not found.");
    }

    public string GetRawJson(string configurationName)
    {
        if (_configuration.TryGetValue(configurationName.ToLower(), out var value))
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }

        throw new InvalidOperationException($"Configuration parameter '{configurationName}' not found.");
    }

    public IEnumerable<string> GetAllRawJsonByCkTypeId(string ckTypeId)
    {
        if (_configurationByCkTypeId.TryGetValue(ckTypeId.ToLower(), out var values))
        {
            return values.Select(v => System.Text.Json.JsonSerializer.Serialize(v));
        }

        return Enumerable.Empty<string>();
    }
}
