using System;
using System.Linq;
using System.Text.Json;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Pins that the zero-copy element-direct <c>Get&lt;T&gt;</c>/<c>GetArray&lt;T&gt;</c> path (taken on a
/// clean root base) is observationally identical to the node path (taken once the overlay lifts).
/// A clean context reads element-direct; a context with an unrelated write forces <c>HasWrites</c>
/// so reads route through the node fall-back. Both must agree — this is the equivalence the change
/// rests on (and it was already true before, since both were the node path). Also pins the
/// borrowed-element lifetime contract for <c>GetArray</c> (must be eagerly materialised).
/// </summary>
public class GetTypedElementVsNodeParityTests
{
    private const string Json =
        """
        {"i":42,"l":2147483648,"d":3.5,"f":1.25,"dec":0.6511560000001,"b":true,"s":"hi",
         "dt":"2024-01-02T03:04:05Z","dto":"2024-01-02T03:04:05+02:00",
         "g":"a1b2c3d4-0000-1111-2222-333344445555","obj":{"X":7,"Y":"z"},
         "arr":[1,2,3],"objs":[{"X":1,"Y":"a"},{"X":2,"Y":"b"}],"nul":null}
        """;

    public sealed record Point(int X, string Y);

    private static IDataContext Clean() => new DataContextImpl(JsonDocument.Parse(Json).RootElement);

    private static IDataContext Lifted()
    {
        var ctx = new DataContextImpl(JsonDocument.Parse(Json).RootElement);
        ctx.Set("$.__lift", 1); // unrelated write flips HasWrites → reads take the node path
        return ctx;
    }

    private static void AssertParity<T>(string path, Func<IDataContext, T?> read)
    {
        using var clean = Clean();
        using var lifted = Lifted();
        Assert.Equal(read(clean), read(lifted));
    }

    [Fact] public void Int() => AssertParity("$.i", c => c.Get<int>("$.i"));
    [Fact] public void Long() => AssertParity("$.l", c => c.Get<long>("$.l"));
    [Fact] public void Double() => AssertParity("$.d", c => c.Get<double>("$.d"));
    [Fact] public void Float() => AssertParity("$.f", c => c.Get<float>("$.f"));
    [Fact] public void Decimal() => AssertParity("$.dec", c => c.Get<decimal>("$.dec"));
    [Fact] public void Bool() => AssertParity("$.b", c => c.Get<bool>("$.b"));
    [Fact] public void Str() => AssertParity("$.s", c => c.Get<string>("$.s"));
    [Fact] public void Dt() => AssertParity("$.dt", c => c.Get<DateTime>("$.dt"));
    [Fact] public void Dto() => AssertParity("$.dto", c => c.Get<DateTimeOffset>("$.dto"));
    [Fact] public void Guid_() => AssertParity("$.g", c => c.Get<Guid>("$.g"));
    [Fact] public void Record() => AssertParity("$.obj", c => c.Get<Point>("$.obj"));
    [Fact] public void NullableInt_OnNull() => AssertParity("$.nul", c => c.Get<int?>("$.nul"));
    [Fact] public void Record_OnNull() => AssertParity("$.nul", c => c.Get<Point>("$.nul"));
    [Fact] public void Missing() => AssertParity("$.absent", c => c.Get<int?>("$.absent"));

    // complex T compared by serialized form (record uses value equality, but pin shape too)
    [Fact]
    public void RecordArray_Parity()
    {
        using var clean = Clean();
        using var lifted = Lifted();
        var a = clean.GetArray<Point>("$.objs")!.ToArray();
        var b = lifted.GetArray<Point>("$.objs")!.ToArray();
        Assert.Equal(b, a);
    }

    [Theory]
    [InlineData("$.arr")]   // array → list
    [InlineData("$.i")]     // scalar → singleton
    [InlineData("$.obj")]   // object → null
    [InlineData("$.nul")]   // explicit null → null
    [InlineData("$.absent")]// absent → null
    public void GetArrayInt_Shape_Parity(string path)
    {
        using var clean = Clean();
        using var lifted = Lifted();
        var a = clean.GetArray<int>(path)?.ToArray();
        var b = lifted.GetArray<int>(path)?.ToArray();
        Assert.Equal(b, a);
    }

    [Fact]
    public void GetArray_Result_SurvivesBackingDocumentDispose()
    {
        // Borrowed-element lifetime: the element-direct GetArray must eagerly materialise, so the
        // result is valid after the source's backing JsonDocument is disposed.
        var doc = JsonDocument.Parse(Json);
        var ctx = new DataContextImpl(doc.RootElement);
        var result = ctx.GetArray<int>("$.arr");
        ctx.Dispose();
        doc.Dispose(); // pooled UTF-8 buffer returned
        Assert.Equal(new[] { 1, 2, 3 }, result!.ToArray()); // no ObjectDisposedException
    }
}
