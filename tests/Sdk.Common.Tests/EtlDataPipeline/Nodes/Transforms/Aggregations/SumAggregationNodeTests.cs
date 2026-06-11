#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Aggregations;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms.Aggregations;

public class SumAggregationNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(SumAggregationNodeConfiguration sumAggregationNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orderItems = new[]
            {
                new { type = "product", price = 100.0, quantity = 2, status = "active" },
                new { type = "product", price = 50.0, quantity = 1, status = "active" },
                new { type = "service", price = 25.0, quantity = 3, status = "active" },
                new { type = "product", price = 75.0, quantity = 1, status = "inactive" }
            },
            adjustments = new[]
            {
                new { type = "discount", amount = 10.0, category = "promotional" },
                new { type = "discount", amount = 5.0, category = "loyalty" },
                new { type = "tax", amount = 15.0, category = "vat" },
                new { type = "fee", amount = 3.0, category = "processing" }
            },
            inventory = new[]
            {
                new { productId = "A1", unitCost = 20.0, stock = 10 },
                new { productId = "B2", unitCost = 15.5, stock = 25 },
                new { productId = "C3", unitCost = 8.75, stock = 50 }
            },
            financials = new
            {
                revenues = new[] { 1000.0, 1500.0, 2000.0 },
                expenses = new[] { 500.0, 750.0, 900.0 },
                taxes = new[] { 100.0, 150.0, 200.0 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SumAggregation", 0, sumAggregationNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_SimpleSum_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.totalPrice",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.price",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(250.0, dataContext.Get<double>("$.totalPrice"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithMultiplier_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.discountedTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.price",
                    Value = 0.9
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(225.0, dataContext.Get<double>("$.discountedTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MultipleAggregations_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.combinedTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.price",
                    Value = 1.0
                },
                new SumAggregationItem
                {
                    Path = "$.adjustments[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.amount",
                    Value = -0.5
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(233.5, dataContext.Get<double>("$.combinedTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ArrayAggregation_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.totalRevenue",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.financials",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.revenues[*]",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(4500.0, dataContext.Get<double>("$.totalRevenue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WeightedCalculation_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.inventoryValue",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.inventory[0]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.unitCost",
                    Value = 10
                },
                new SumAggregationItem
                {
                    Path = "$.inventory[1]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.unitCost",
                    Value = 25
                },
                new SumAggregationItem
                {
                    Path = "$.inventory[2]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.unitCost",
                    Value = 50
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1025.0, dataContext.Get<double>("$.inventoryValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyPath_ReturnsZero()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.emptyTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.nonexistent[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.price",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0.0, dataContext.Get<double>("$.emptyTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullInputData_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("null"));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);

        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.total",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.items[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.value",
                    Value = 1.0
                }
            }
        };

        var nodeContext = rootNodeContext.RegisterChildNode("SumAggregation", 0, sumAggregationNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await Assert.ThrowsAnyAsync<Exception>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NegativeMultiplier_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.netProfit",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.financials",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.revenues[*]",
                    Value = 1.0
                },
                new SumAggregationItem
                {
                    Path = "$.financials",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.expenses[*]",
                    Value = -1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(2350.0, dataContext.Get<double>("$.netProfit"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ZeroMultiplier_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.zeroTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.price",
                    Value = 0.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0.0, dataContext.Get<double>("$.zeroTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyFilterPath_IncludesAll()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.allItemsTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = "",
                    ComparisonValue = "anything",
                    AggregationPath = "$.price",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(250.0, dataContext.Get<double>("$.allItemsTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithStringFilter_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.activeTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = "$.status",
                    ComparisonValue = "active",
                    AggregationPath = "$.price",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(175.0, dataContext.Get<double>("$.activeTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithTypeFilter_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.productTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = "$.type",
                    ComparisonValue = "product",
                    AggregationPath = "$.price",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(225.0, dataContext.Get<double>("$.productTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_FilteredMultipleAggregations_OK()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.netTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = "$.status",
                    ComparisonValue = "active",
                    AggregationPath = "$.price",
                    Value = 1.0
                },
                new SumAggregationItem
                {
                    Path = "$.adjustments[*]",
                    FilterPath = "$.type",
                    ComparisonValue = "discount",
                    AggregationPath = "$.amount",
                    Value = -1.0
                },
                new SumAggregationItem
                {
                    Path = "$.adjustments[*]",
                    FilterPath = "$.type",
                    ComparisonValue = "tax",
                    AggregationPath = "$.amount",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(175.0, dataContext.Get<double>("$.netTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoFilterMatches_ReturnsZero()
    {
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.noMatchTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = "$.status",
                    ComparisonValue = "nonexistent",
                    AggregationPath = "$.price",
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0.0, dataContext.Get<double>("$.noMatchTotal"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NonNumericMatch_Throws()
    {
        // SumAggregationNode previously surfaced non-numeric matches via
        // PipelineExecutionException (original Newtonsoft code used
        // JToken.ToObject<double>() which threw on non-numeric values).
        // The STJ migration replaced that throw with TryGet + silent-skip,
        // making aggregation results quietly wrong rather than loud-failing on
        // bad input. This test pins the loud-fail contract.

        // The seed data has "type" (a string like "product") as a string field;
        // pointing AggregationPath at "$.type" gives a non-numeric, non-parseable value.
        SumAggregationNodeConfiguration sumAggregationNodeConfiguration = new()
        {
            TargetPath = "$.badTotal",
            DocumentMode = DocumentModes.Extend,
            TargetValueKind = ValueKinds.Simple,
            TargetValueWriteMode = TargetValueWriteModes.Overwrite,
            Aggregations = new[]
            {
                new SumAggregationItem
                {
                    Path = "$.orderItems[*]",
                    FilterPath = null,
                    ComparisonValue = null,
                    AggregationPath = "$.type",  // "product" / "service" — not numeric
                    Value = 1.0
                }
            }
        };

        var (dataContext, nodeContext) = PrepareTest(sumAggregationNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SumAggregationNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }
}
