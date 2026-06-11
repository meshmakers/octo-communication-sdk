using System.Reflection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;

internal static class TypeExtensions
{
    private static NodeConfigurationAttribute GetNodeConfigurationAttribute(this Type type)
    {
        var customAttribute = type.GetCustomAttribute<NodeConfigurationAttribute>();
        if (customAttribute == null)
        {
            throw new InvalidOperationException($"Type '{type.FullName}' does not have a NodeConfigurationAttribute");
        }

        return customAttribute;
    }
    
    private static NodeNameAttribute GetNodeNameAttribute(this Type type)
    {
        var customAttribute = type.GetCustomAttribute<NodeNameAttribute>();
        if (customAttribute == null)
        {
            throw new InvalidOperationException($"Type '{type.FullName}' does not have a NodeNameAttribute");
        }

        return customAttribute;
    }
    
    public static string GetConfigurationQualifiedName(this Type type)
    {
        var customAttribute = type.GetNodeNameAttribute();
        return customAttribute.Name + "@" + customAttribute.Version;
    }
    
    public static Type GetNodeConfigurationType(this Type type)
    {
        var customAttribute = type.GetNodeConfigurationAttribute();
        return customAttribute.NodeConfigurationType;
    }
}