using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Characterization tests pinning the CURRENT behaviour of the under-tested
/// <c>DataContextChild</c> path for document-mode Replace and array-wrap writes.
/// These tests must pass against the current code; they exist so a later refactor
/// of <c>IDataContext</c> cannot silently regress child semantics.
/// </summary>
public class ChildDocumentModeTests
{
    private static JsonElement Doc(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Child_Set_Replace_ResetsChildOverlay_ParentUntouched()
    {
        var parent = new DataContextImpl(Doc("{\"keep\":1}"));
        var child = parent.CreateIterationChild(new[] { ("$.key", Doc("{\"a\":1}")) });

        child.Set("$", Doc("{\"fresh\":2}"), DocumentModes.Replace, ValueKinds.Simple,
            TargetValueWriteModes.Overwrite);

        // The freshly written root value is visible.
        Assert.Equal(2, child.Get<int>("$.fresh"));

        // CURRENT BEHAVIOUR: Replace resets only the child OVERLAY (it writes "$" = {} then
        // the new root value). It does NOT clear the alias map. The alias "$.key" was resolved
        // up front in CreateIterationChild and is consulted by TryReadAlias as a fallback once
        // the overlay root ({"fresh":2}) has no "key" property. So $.key still resolves to the
        // alias object — GetKind reports Object, not Undefined.
        Assert.Equal(DataKind.Object, child.GetKind("$.key"));
        Assert.Equal(1, child.Get<int>("$.key.a"));

        // Parent is untouched by any child write.
        Assert.Equal(1, parent.Get<int>("$.keep"));
    }

    [Fact]
    public void Child_Set_Array_WrapsValueInArray()
    {
        var parent = new DataContextImpl(Doc("{}"));
        var child = parent.CreateIterationChild(System.Array.Empty<(string, JsonElement)>());

        child.Set("$.items", 7, DocumentModes.Extend, ValueKinds.Array,
            TargetValueWriteModes.Overwrite);

        // ValueKinds.Array wraps the scalar 7 in a single-element array: [7].
        Assert.Equal(1, child.Length("$.items"));
        Assert.Equal(7, child.Get<int>("$.items[0]"));
    }
}
