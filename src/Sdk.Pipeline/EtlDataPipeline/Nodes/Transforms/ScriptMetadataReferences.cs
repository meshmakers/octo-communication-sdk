using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Process-wide, lazily built and thread-safe set of Roslyn <see cref="MetadataReference"/>s that
/// every <c>ExecuteCSharp</c> compile shares (AB#7063).
/// <para>
/// Why: handing Roslyn plain assembly paths makes it create a fresh reference for every path on every
/// compile, and each one copies the assembly metadata into native memory. With ~250 loaded assemblies
/// that is 350-750 MB of unmanaged working set per distinct script, freed only after a gen2 GC.
/// Re-using the same <see cref="MetadataReference"/> instances lets Roslyn share that metadata.
/// </para>
/// <para>
/// Rebuild rule: the set is derived from the non-dynamic AppDomain assemblies that have a location.
/// It is re-evaluated on every call, but a new array is only produced when that set of locations has
/// changed (an assembly was loaded after the previous compile), so a script compiled later still sees
/// assemblies loaded since an earlier compile. Unchanged assemblies keep their
/// <see cref="MetadataReference"/> instance, so a rebuild only creates references for the new ones.
/// </para>
/// </summary>
internal sealed class ScriptMetadataReferences
{
    /// <summary>The instance used by <see cref="ExecuteCSharpNode"/>.</summary>
    internal static ScriptMetadataReferences Shared { get; } = new(GetAppDomainAssemblyLocations, CreateFromFile);

    private readonly Func<IReadOnlyCollection<string>> _locationProvider;
    private readonly Func<string, MetadataReference> _referenceFactory;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MetadataReference> _referencesByLocation = new(StringComparer.Ordinal);

    private MetadataReference[]? _current;
    private int _buildCount;

    internal ScriptMetadataReferences(Func<IReadOnlyCollection<string>> locationProvider,
        Func<string, MetadataReference> referenceFactory)
    {
        _locationProvider = locationProvider;
        _referenceFactory = referenceFactory;
    }

    /// <summary>Test seam: how many times the reference array was (re)built.</summary>
    internal int BuildCount
    {
        get
        {
            lock (_gate)
            {
                return _buildCount;
            }
        }
    }

    /// <summary>
    /// Returns the shared reference array. The same array instance is returned until the set of
    /// loaded assemblies changes. Callers must not modify it.
    /// </summary>
    internal IReadOnlyList<MetadataReference> GetReferences()
    {
        var locations = _locationProvider();

        lock (_gate)
        {
            if (_current is not null && locations.Count == _current.Length && AllKnown(locations))
            {
                return _current;
            }

            var list = new List<MetadataReference>(locations.Count);
            foreach (var location in locations)
            {
                if (!_referencesByLocation.TryGetValue(location, out var reference))
                {
                    reference = _referenceFactory(location);
                    _referencesByLocation[location] = reference;
                }

                list.Add(reference);
            }

            _current = list.ToArray();
            _buildCount++;
            return _current;
        }
    }

    // Locations are distinct, so equal count plus "every location already has a reference that is
    // part of the current array" means an unchanged set.
    private bool AllKnown(IReadOnlyCollection<string> locations)
    {
        var current = new HashSet<MetadataReference>(_current!);
        foreach (var location in locations)
        {
            if (!_referencesByLocation.TryGetValue(location, out var reference) || !current.Contains(reference))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyCollection<string> GetAppDomainAssemblyLocations() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static MetadataReference CreateFromFile(string location) => MetadataReference.CreateFromFile(location);
}
