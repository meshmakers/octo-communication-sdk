using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Tells which attributes of a configuration CK type are <c>Secret</c> (AB#5538). The communication
/// controller ships configuration entities to the adapter with their Secret values revealed as plain
/// strings; a node that copies such a configuration into the data context
/// (<c>GetPipelineConfigByWellKnownName@1</c>, <c>GetPipelineConfigByCkTypeId@1</c>) registers those
/// values with the execution's <see cref="PipelineSecretRegistry" /> first.
/// </summary>
/// <remarks>
/// The default registration (<see cref="NoConfigurationSecretAttributeResolver" />) knows no CK model and
/// answers <c>null</c>; <see cref="ConfigurationSecrets" /> then falls back to
/// <see cref="ConfigurationSecrets.KnownCredentialAttributeNames" />. The mesh adapter replaces it with
/// a resolver backed by the CK cache.
/// </remarks>
public interface IConfigurationSecretAttributeResolver
{
    /// <summary>
    /// Names of the Secret attributes of <paramref name="ckTypeId" />, top level and record members at
    /// any depth (compared case-insensitively). An empty collection means "the type has no Secret";
    /// <c>null</c> means the type could not be resolved.
    /// </summary>
    /// <param name="tenantId">Tenant of the execution</param>
    /// <param name="ckTypeId">CK type of the configuration entity</param>
    IReadOnlyCollection<string>? GetSecretAttributeNames(string tenantId, RtCkId<CkTypeId> ckTypeId);
}

/// <summary>
/// Default <see cref="IConfigurationSecretAttributeResolver" />: resolves nothing, so the known
/// credential attribute names are masked (fail safe).
/// </summary>
public sealed class NoConfigurationSecretAttributeResolver : IConfigurationSecretAttributeResolver
{
    /// <inheritdoc />
    public IReadOnlyCollection<string>? GetSecretAttributeNames(string tenantId, RtCkId<CkTypeId> ckTypeId) => null;
}

/// <summary>
/// Registers the Secret values of a configuration JSON with the execution's secret registry (AB#5538).
/// </summary>
public static class ConfigurationSecrets
{
    /// <summary>
    /// Credential attribute names of System.Communication 3.40, used when the CK type of a configuration
    /// cannot be resolved.
    /// </summary>
    public static readonly IReadOnlyCollection<string> KnownCredentialAttributeNames =
    [
        "Password", "PrivateKey", "PrivateKeyPassphrase", "ClientSecret", "ApiKey", "BotToken", "AdminPassword",
        "SecretValue"
    ];

    /// <summary>
    /// Registers every string value in <paramref name="configuration" /> whose property name is a Secret
    /// attribute of <paramref name="ckTypeId" /> — at any depth, so both the <c>attributes</c> dictionary
    /// and a typed entity's camelCase properties, and record members, are covered. When the type is
    /// unknown (or not given) the <see cref="KnownCredentialAttributeNames" /> are used and a warning
    /// without values is logged.
    /// </summary>
    /// <returns>The number of values registered</returns>
    public static int Register(INodeContext nodeContext, IConfigurationSecretAttributeResolver? resolver,
        string tenantId, RtCkId<CkTypeId>? ckTypeId, JsonNode? configuration, string configurationName)
    {
        ArgumentNullException.ThrowIfNull(nodeContext);
        if (configuration == null)
        {
            return 0;
        }

        IReadOnlyCollection<string>? names = null;
        if (ckTypeId != null && resolver != null)
        {
            try
            {
                names = resolver.GetSecretAttributeNames(tenantId, ckTypeId);
            }
            catch (Exception e)
            {
                nodeContext.Warning("Secret attributes of configuration '{0}' could not be resolved ({1})",
                    configurationName, e.GetType().Name);
            }
        }

        if (names == null)
        {
            nodeContext.Warning(
                "CK type of configuration '{0}' ({1}) could not be resolved; masking the known credential attributes",
                configurationName, Describe(ckTypeId));
            names = KnownCredentialAttributeNames;
        }

        if (names.Count == 0)
        {
            return 0;
        }

        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return Walk(nodeContext, set, configuration, depth: 0);
    }

    private static string Describe(RtCkId<CkTypeId>? ckTypeId)
    {
        try
        {
            return ckTypeId?.ToString() ?? "unknown";
        }
        catch (Exception)
        {
            // An id without a model or element (a half-initialised value) - the warning must not fail.
            return "unknown";
        }
    }

    private static int Walk(INodeContext nodeContext, HashSet<string> names, JsonNode node, int depth)
    {
        if (depth > 32)
        {
            return 0;
        }

        var count = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (value == null)
                    {
                        continue;
                    }

                    if (names.Contains(key) && value is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                    {
                        nodeContext.RegisterSecret(v.GetValue<string>());
                        count++;
                    }
                    else
                    {
                        count += Walk(nodeContext, names, value, depth + 1);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        count += Walk(nodeContext, names, item, depth + 1);
                    }
                }

                break;
        }

        return count;
    }
}
