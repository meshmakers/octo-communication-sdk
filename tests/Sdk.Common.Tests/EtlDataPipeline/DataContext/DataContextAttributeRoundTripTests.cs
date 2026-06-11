using System;
using System.Collections.Generic;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Guards the Newtonsoft→STJ trap: <see cref="RtTypeWithAttributes.Attributes"/> is a get-only
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/>. Without a dedicated converter, STJ serializes
/// the getter but silently discards it on deserialize, so every <c>Get&lt;RtEntity&gt;</c> in the
/// pipeline (ApplyChangesNode, UpdateRtEntityIfNewerNode, the energy nodes …) would receive an
/// entity with empty attributes. These tests drive a real <see cref="DataContextImpl"/> Set→Get
/// round-trip through <see cref="SystemTextJsonOptions.Default"/> — the exact production path.
/// </summary>
public class DataContextAttributeRoundTripTests
{
    private static readonly DateTime SampleUtc = new(2026, 5, 12, 13, 0, 0, DateTimeKind.Utc);

    private static RtEntity BuildEntity()
    {
        var ckTypeId = new RtCkId<CkTypeId>("EnergyIQ/Measurement-1");
        var recordCkId = new RtCkId<CkRecordId>("EnergyIQ/TimeRange-1");

        var timeRange = new RtRecord(recordCkId, new Dictionary<string, object?>
        {
            ["From"] = SampleUtc
        });

        return new RtEntity(ckTypeId, OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            ["energyConsumed"] = 42.3,
            ["TimeRange"] = timeRange
        })
        {
            RtWellKnownName = "wkn-1"
        };
    }

    [Fact]
    public void Set_Then_Get_RtEntity_PreservesScalarAttributes()
    {
        using var ctx = new DataContextImpl(JsonDocument.Parse("{}"));
        ctx.Set("$.entity", BuildEntity());

        var result = ctx.Get<RtEntity>("$.entity");

        Assert.NotNull(result);
        Assert.Equal("wkn-1", result!.RtWellKnownName);
        Assert.NotEmpty(result.Attributes);
        Assert.Equal(42.3, result.GetAttributeValue<double>("energyConsumed"));
    }

    [Fact]
    public void Set_Then_Get_RtEntity_PreservesNestedRtRecord()
    {
        using var ctx = new DataContextImpl(JsonDocument.Parse("{}"));
        ctx.Set("$.entity", BuildEntity());

        var result = ctx.Get<RtEntity>("$.entity");

        Assert.NotNull(result);
        var timeRange = result!.GetRtRecordAttributeValueOrDefault<RtRecord>("TimeRange");
        Assert.NotNull(timeRange);
        Assert.Equal("EnergyIQ", timeRange!.CkRecordId.ModelId);
    }

    [Fact]
    public void Set_Then_Get_RtEntity_PreservesDateTimeAttribute()
    {
        using var ctx = new DataContextImpl(JsonDocument.Parse("{}"));
        ctx.Set("$.entity", BuildEntity());

        var result = ctx.Get<RtEntity>("$.entity");
        var timeRange = result!.GetRtRecordAttributeValueOrDefault<RtRecord>("TimeRange");

        Assert.NotNull(timeRange);
        Assert.Equal(SampleUtc, timeRange!.GetAttributeValue<DateTime>("From").ToUniversalTime());
    }

    [Fact]
    public void Set_Then_Get_RtEntity_MaterializesScalarClrTypes()
    {
        // CLR-type round-trip parity with Newtonsoft, enforced as the contract by
        // Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests. Small integers
        // box to Int32 (matching JObject.FromObject(int) → JValue with Value=Int32), reals
        // stay double, integral doubles survive the round-trip as double (the
        // NewtonsoftParityDoubleConverter writes ".0"), ISO-8601 strings become DateTime,
        // plain strings stay strings. Downstream GetAttributeValue<T> and the MongoDB BSON
        // serializer (which dispatches on CLR type) depend on these.
        var ckTypeId = new RtCkId<CkTypeId>("EnergyIQ/Measurement-1");
        var entity = new RtEntity(ckTypeId, OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            ["count"] = 7,
            ["wholeReal"] = 0.0,
            ["largeId"] = (long)int.MaxValue + 1L,
            ["ratio"] = 3.5,
            ["name"] = "meter-a",
            ["measuredAt"] = SampleUtc
        });

        using var ctx = new DataContextImpl(JsonDocument.Parse("{}"));
        ctx.Set("$.entity", entity);
        var result = ctx.Get<RtEntity>("$.entity")!;

        Assert.IsType<int>(result.Attributes["count"]);
        Assert.IsType<double>(result.Attributes["wholeReal"]);
        Assert.IsType<long>(result.Attributes["largeId"]);
        Assert.IsType<double>(result.Attributes["ratio"]);
        Assert.IsType<string>(result.Attributes["name"]);
        Assert.IsType<DateTime>(result.Attributes["measuredAt"]);
        Assert.Equal(SampleUtc, ((DateTime)result.Attributes["measuredAt"]!).ToUniversalTime());
    }

    [Fact]
    public void Set_Then_Get_EntityUpdateInfoList_PreservesAttributes()
    {
        // Mirrors the actual load-node path: Get<List<EntityUpdateInfo<RtEntity>>>.
        var updates = new List<EntityUpdateInfo<RtEntity>>
        {
            EntityUpdateInfo<RtEntity>.CreateInsert(BuildEntity())
        };

        using var ctx = new DataContextImpl(JsonDocument.Parse("{}"));
        ctx.Set("$.updates", updates);

        var result = ctx.Get<List<EntityUpdateInfo<RtEntity>>>("$.updates");

        Assert.NotNull(result);
        var entity = Assert.Single(result!).RtEntity;
        Assert.NotNull(entity);
        Assert.Equal(42.3, entity!.GetAttributeValue<double>("energyConsumed"));
    }
}
