using System.Reflection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

internal class EdgeDataBufferConfiguration
{
    public EdgeDataBufferConfiguration()
    {
        var appName = Assembly.GetEntryAssembly()?.GetName().Name;
        var appPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        StoragePath = Path.Combine(appPath, $".{appName}");
    }

    public string StoragePath { get; set; }
}