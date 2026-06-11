using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Phase-0 characterization: pins child <see cref="IDataContext.UpdateMatchesAsync"/>
/// write-back WITHOUT the manual <c>child.Set("$", ...)</c> seed that the existing
/// <c>ChildContextTests</c> uses. The contract this locks is what the
/// DataContextImpl/DataContextChild unification must preserve.
/// </summary>
public class ChildUpdateMatchesTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Child_UpdateMatches_AliasBackedMatches_WritesReachOverlay()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(
            new[] { ("$.items", Doc("[{\"v\":1},{\"v\":2}]")) });

        await child.UpdateMatchesAsync("$.items[*]", async sub =>
        {
            var v = sub.Get<int>("$.v");
            sub.Set("$.v", v * 10);
            await Task.CompletedTask;
        });

        // CURRENT BEHAVIOUR: child UpdateMatchesAsync write-back reaches the child's
        // overlay even when the matched data comes purely from an alias (no manual
        // "$" seed). The child's UpdateMatchesAsync evaluates against an
        // alias-augmented eval root (LayeredSource.BuildEvalRoot) so "$.items[*]" finds
        // the alias-backed array, then writes results back via Set(canonicalPath, ...).
        Assert.Equal(10, child.Get<int>("$.items[0].v"));
        Assert.Equal(20, child.Get<int>("$.items[1].v"));
    }
}
