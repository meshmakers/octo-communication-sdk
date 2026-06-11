using System.Reflection;

namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Loads the synthetic JSON input documents embedded in the test assembly.
/// Each input document mirrors a common production shape and is paired with
/// the <see cref="PathExpressions"/> corpus by <c>ReadParityTests</c>.
/// </summary>
public static class ParityCorpus
{
    /// <summary>
    /// Enumerates the embedded JSON documents as (resourceName, jsonText) tuples.
    /// </summary>
    public static IEnumerable<(string Name, string Json)> Inputs()
    {
        var asm = typeof(ParityCorpus).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded resource: {name}");
            using var reader = new StreamReader(stream);
            yield return (ShortName(name), reader.ReadToEnd());
        }
    }

    private static string ShortName(string manifestName)
    {
        // e.g. "Sdk.Common.PipelineParityTests.TestData.cust-orders.json" -> "cust-orders.json"
        const string prefix = "Sdk.Common.PipelineParityTests.TestData.";
        return manifestName.StartsWith(prefix, StringComparison.Ordinal)
            ? manifestName.Substring(prefix.Length)
            : manifestName;
    }
}
