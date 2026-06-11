using System;
using System.Collections.Generic;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.DataContext;

/// <summary>
/// Guards the remaining <see cref="RtTypeWithAttributes"/> subclasses (besides RtEntity/RtRecord)
/// against the get-only-attributes STJ trap. Neither is deserialized via <c>Get&lt;T&gt;</c> in the
/// pipeline today, but both carry the same hazard, so these pin the contract: STJ must round-trip
/// their attributes through <see cref="SystemTextJsonOptions.Default"/>.
/// </summary>
public class RtTypeWithAttributesStjRoundTripTests
{
    [Fact]
    public void Stj_RoundTrip_RtAssociation_PreservesAttributes()
    {
        var association = new RtAssociation(
            new RtCkId<CkAssociationRoleId>("System/ParentChild"),
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["weight"] = 1.5 })
        {
            OriginRtId = OctoObjectId.GenerateNewId(),
            TargetRtId = OctoObjectId.GenerateNewId()
        };

        var json = JsonSerializer.Serialize(association, SystemTextJsonOptions.Default);
        var result = JsonSerializer.Deserialize<RtAssociation>(json, SystemTextJsonOptions.Default)!;

        Assert.NotEmpty(result.Attributes);
        Assert.Equal(1.5, result.GetAttributeValue<double>("weight"));
    }

    [Fact]
    public void Stj_RoundTrip_StreamDataEntity_PreservesAttributes()
    {
        var entity = new StreamDataEntity(new Dictionary<string, object?> { ["value"] = 12.3 })
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>("EnergyIQ/Measurement-1"),
            Timestamp = new DateTime(2026, 5, 12, 13, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(entity, SystemTextJsonOptions.Default);
        var result = JsonSerializer.Deserialize<StreamDataEntity>(json, SystemTextJsonOptions.Default)!;

        Assert.NotEmpty(result.Attributes);
        Assert.Equal(12.3, result.GetAttributeValue<double>("value"));
    }
}
