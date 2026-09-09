#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class JoinNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(JoinNodeConfiguration joinNodeConfiguration, object? testData = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = testData ?? new
        {
            orders = new[]
            {
                new { orderId = "123", customerName = "John Doe", customerId = "c1" },
                new { orderId = "456", customerName = "Jane Smith", customerId = "c2" },
                new { orderId = "789", customerName = "Bob Wilson", customerId = "c3" }
            },
            orderItems = new[]
            {
                new { orderId = "123", productName = "Widget", quantity = 2, price = 10.50 },
                new { orderId = "123", productName = "Gadget", quantity = 1, price = 25.00 },
                new { orderId = "456", productName = "Tool", quantity = 3, price = 15.75 },
                new { orderId = "456", productName = "Widget", quantity = 1, price = 10.50 },
                new { orderId = "999", productName = "Orphaned", quantity = 1, price = 5.00 }
            },
            customers = new[]
            {
                new { customerId = "c1", address = "123 Main St", city = "New York" },
                new { customerId = "c2", address = "456 Oak Ave", city = "Chicago" },
                new { customerId = "c3", address = "789 Pine Rd", city = "Seattle" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_BasicJoin_OK()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(2, dataContext.Length("$.orders[0].items"));
        Assert.Equal("Widget", dataContext.Get<string>("$.orders[0].items[0].productName"));
        Assert.Equal("Gadget", dataContext.Get<string>("$.orders[0].items[1].productName"));

        Assert.Equal(2, dataContext.Length("$.orders[1].items"));
        Assert.Equal("Tool", dataContext.Get<string>("$.orders[1].items[0].productName"));
        Assert.Equal("Widget", dataContext.Get<string>("$.orders[1].items[1].productName"));

        Assert.Equal(0, dataContext.Length("$.orders[2].items"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MultipleMatches_OK()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[0]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.matchedItems"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(2, dataContext.Length("$.orders[0].matchedItems"));
        Assert.Equal("123", dataContext.Get<string>("$.orders[0].matchedItems[0].orderId"));
        Assert.Equal(2, dataContext.Get<int>("$.orders[0].matchedItems[0].quantity"));
        Assert.Equal(10.50, dataContext.Get<double>("$.orders[0].matchedItems[0].price"));

        Assert.Equal("123", dataContext.Get<string>("$.orders[0].matchedItems[1].orderId"));
        Assert.Equal(1, dataContext.Get<int>("$.orders[0].matchedItems[1].quantity"));
        Assert.Equal(25.00, dataContext.Get<double>("$.orders[0].matchedItems[1].price"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoMatches_EmptyArray()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[2]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(0, dataContext.Length("$.orders[2].items"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DifferentJoinPath_OK()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.customerId",
            JoinPath = "$.customers[*]",
            JoinKeyPath = "$.customerId",
            ItemPath = "$.customerDetails"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(1, dataContext.Length("$.orders[0].customerDetails"));
        Assert.Equal("123 Main St", dataContext.Get<string>("$.orders[0].customerDetails[0].address"));
        Assert.Equal("New York", dataContext.Get<string>("$.orders[0].customerDetails[0].city"));

        Assert.Equal(1, dataContext.Length("$.orders[1].customerDetails"));
        Assert.Equal("456 Oak Ave", dataContext.Get<string>("$.orders[1].customerDetails[0].address"));
        Assert.Equal("Chicago", dataContext.Get<string>("$.orders[1].customerDetails[0].city"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullInputData_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("null"));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoSourceData_ThrowsException()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.nonexistent[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoJoinData_SetsEmptyArrays()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.nonexistent[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0, dataContext.Length("$.orders[0].items"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyJoinArray_SetsEmptyArrays()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orders = new[]
            {
                new { orderId = "123", customerName = "John Doe" },
                new { orderId = "456", customerName = "Jane Smith" }
            },
            orderItems = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0, dataContext.Length("$.orders[0].items"));
        Assert.Equal(0, dataContext.Length("$.orders[1].items"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyKeyValue_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orders = new[]
            {
                new { orderId = "", customerName = "John Doe" },
            },
            orderItems = new[]
            {
                new { orderId = "123", productName = "Widget" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullKeyValue_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orders = new[]
            {
                new { orderId = (string?)null, customerName = "John Doe" },
            },
            orderItems = new[]
            {
                new { orderId = (string?)"123", productName = "Widget" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_MissingKeyPath_ThrowsException()
    {
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.nonexistentKey",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_CaseSensitiveMatching_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orders = new[]
            {
                new { orderId = "ABC", customerName = "John Doe" },
            },
            orderItems = new[]
            {
                new { orderId = "abc", productName = "Widget" },
                new { orderId = "ABC", productName = "Gadget" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(1, dataContext.Length("$.orders[0].items"));
        Assert.Equal("Gadget", dataContext.Get<string>("$.orders[0].items[0].productName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NumericKeys_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orders = new[]
            {
                new { orderId = 123, customerName = "John Doe" },
                new { orderId = 456, customerName = "Jane Smith" }
            },
            orderItems = new[]
            {
                new { orderId = 123, productName = "Widget" },
                new { orderId = 456, productName = "Gadget" },
                new { orderId = 123, productName = "Tool" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(2, dataContext.Length("$.orders[0].items"));
        Assert.Equal(1, dataContext.Length("$.orders[1].items"));
    }

    [Fact]
    public async Task ProcessObjectAsync_JoinKeyWithArrayIndex_OK()
    {
        // The source-side key (KeyPath) is resolved with the full JSONPath dialect, but the
        // join-side key was resolved by a hand-rolled dotted-property walker that silently
        // returns null for any bracket/index segment — so a JoinKeyPath like "$.keys[0]"
        // matched nothing and produced an empty join. The two sides must use the same
        // resolver. Pre-migration the join used SelectToken (full dialect) on both sides.
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            orders = new[]
            {
                new { matchKey = "k1", customerName = "John Doe" }
            },
            lookups = new[]
            {
                new { keys = new[] { "k1" }, info = "Info1" },
                new { keys = new[] { "k2" }, info = "Info2" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.matchKey",
            JoinPath = "$.lookups[*]",
            JoinKeyPath = "$.keys[0]",
            ItemPath = "$.matched"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(1, dataContext.Length("$.orders[0].matched"));
        Assert.Equal("Info1", dataContext.Get<string>("$.orders[0].matched[0].info"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NestedPaths_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            data = new
            {
                orders = new[]
                {
                    new { details = new { id = "123" }, customerName = "John Doe" }
                },
                items = new[]
                {
                    new { order = new { id = "123" }, productName = "Widget" }
                }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.data.orders[*]",
            KeyPath = "$.details.id",
            JoinPath = "$.data.items[*]",
            JoinKeyPath = "$.order.id",
            ItemPath = "$.products"
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Join", 0, joinNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        Assert.Equal(1, dataContext.Length("$.data.orders[0].products"));
        Assert.Equal("Widget", dataContext.Get<string>("$.data.orders[0].products[0].productName"));
    }

    // The two opt-in options: allowMissingKey turns a source object without a usable key into a
    // left-join row with an empty item array (instead of the ValueNotSet exception pinned by the
    // three *_ThrowsException tests above); noMatchHandling Fail turns a key without a match into
    // an exception naming the key and the join path (instead of the silent empty array). Both
    // defaults keep the pre-existing behavior.

    [Fact]
    public async Task ProcessObjectAsync_Defaults_MissingKeyThrowsAndNoMatchStaysEmptyArray()
    {
        // A configuration that sets neither option resolves to allowMissingKey=false and
        // noMatchHandling=Ignore, and the node behaves exactly as before the options existed.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };
        Assert.False(joinNodeConfiguration.AllowMissingKey);
        Assert.Equal(JoinNoMatchHandlingDto.Ignore, joinNodeConfiguration.NoMatchHandling);

        // No match (order 789 has no items): silent empty array, downstream nodes run.
        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(2, dataContext.Length("$.orders[0].items"));
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.orders[2].items"));
        Assert.Equal(0, dataContext.Length("$.orders[2].items"));

        // Missing key: still the ValueNotSet exception, downstream nodes do not run.
        var (missingKeyDataContext, missingKeyNodeContext) = PrepareTest(joinNodeConfiguration with
        {
            KeyPath = "$.nonexistentKey"
        });
        var missingKeyFn = A.Fake<NodeDelegate>();
        var missingKeyTestee = new JoinNode(missingKeyFn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => missingKeyTestee.ProcessObjectAsync(missingKeyDataContext, missingKeyNodeContext));

        Assert.Contains("Value not set", exception.Message);
        Assert.Contains("$.nonexistentKey", exception.Message);
        A.CallTo(() => missingKeyFn.Invoke(A<IDataContext>._, A<INodeContext>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_MissingKeyPath_AllowMissingKey_SetsEmptyArray()
    {
        // Counterpart to ProcessObjectAsync_MissingKeyPath_ThrowsException.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.nonexistentKey",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            AllowMissingKey = true
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(DataKind.Array, dataContext.GetKind($"$.orders[{i}].items"));
            Assert.Equal(0, dataContext.Length($"$.orders[{i}].items"));
        }

        // The source objects themselves are untouched apart from the new array.
        Assert.Equal("John Doe", dataContext.Get<string>("$.orders[0].customerName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullKeyValue_AllowMissingKey_SetsEmptyArray()
    {
        // Counterpart to ProcessObjectAsync_NullKeyValue_ThrowsException.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            AllowMissingKey = true
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[]
            {
                new { orderId = (string?)null, customerName = "John Doe" },
                new { orderId = (string?)"123", customerName = "Jane Smith" }
            },
            orderItems = new[]
            {
                new { orderId = (string?)"123", productName = "Widget" }
            }
        });
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.orders[0].items"));
        Assert.Equal(0, dataContext.Length("$.orders[0].items"));
        // A source object with a key is still joined normally.
        Assert.Equal(1, dataContext.Length("$.orders[1].items"));
        Assert.Equal("Widget", dataContext.Get<string>("$.orders[1].items[0].productName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyKeyValue_AllowMissingKey_SetsEmptyArray()
    {
        // Counterpart to ProcessObjectAsync_EmptyKeyValue_ThrowsException.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            AllowMissingKey = true
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[]
            {
                new { orderId = "", customerName = "John Doe" },
                new { orderId = "123", customerName = "Jane Smith" }
            },
            orderItems = new[]
            {
                new { orderId = "123", productName = "Widget" }
            }
        });
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.orders[0].items"));
        Assert.Equal(0, dataContext.Length("$.orders[0].items"));
        Assert.Equal(1, dataContext.Length("$.orders[1].items"));
        Assert.Equal("Widget", dataContext.Get<string>("$.orders[1].items[0].productName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoMatches_NoMatchHandlingFail_ThrowsException()
    {
        // Counterpart to ProcessObjectAsync_NoMatches_EmptyArray: order 789 has a key but no
        // item; with Fail the run ends with an exception naming the key and the join path.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));

        Assert.Contains("'789'", exception.Message);
        Assert.Contains("'$.orderId'", exception.Message);
        Assert.Contains("'$.orderItems[*]'", exception.Message);
        A.CallTo(() => fn.Invoke(A<IDataContext>._, A<INodeContext>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_NoMatches_AllowMissingKey_NoMatchHandlingFail_ThrowsException()
    {
        // allowMissingKey only covers the "no key" case: a keyed source object without a match
        // still fails under Fail.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            AllowMissingKey = true,
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));

        Assert.Contains("'789'", exception.Message);
        A.CallTo(() => fn.Invoke(A<IDataContext>._, A<INodeContext>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_NumericKeyNoMatch_NoMatchHandlingFail_MessageNamesKey()
    {
        // A numeric key is named by its JSON text.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[] { new { orderId = 42, customerName = "John Doe" } },
            orderItems = new[] { new { orderId = 7, productName = "Widget" } }
        });
        var testee = new JoinNode(A.Fake<NodeDelegate>());

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));

        Assert.Contains("'42'", exception.Message);
    }

    [Fact]
    public async Task ProcessObjectAsync_NonScalarKeyNoMatch_NoMatchHandlingFail_MessageOmitsValue()
    {
        // A misconfigured keyPath that points at an object must not put the object's content
        // into the error text: the message travels into logs and, under ForEach continueOnError
        // with errorsPath, into the pipeline document.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.customer",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.customer",
            ItemPath = "$.items",
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[] { new { customer = new { name = "John Doe", email = "john@example.com" } } },
            orderItems = new[] { new { customer = new { name = "Jane Smith", email = "jane@example.com" } } }
        });
        var testee = new JoinNode(A.Fake<NodeDelegate>());

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));

        Assert.Contains("'$.customer'", exception.Message);
        Assert.Contains("'$.orderItems[*]'", exception.Message);
        Assert.Contains("<non-scalar key>", exception.Message);
        Assert.DoesNotContain("john@example.com", exception.Message);
        Assert.DoesNotContain("John Doe", exception.Message);
    }

    [Fact]
    public async Task ProcessObjectAsync_AllKeysMatch_NoMatchHandlingFail_OK()
    {
        // Fail only reacts to a key without a match; a fully matched join is unaffected.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.customerId",
            JoinPath = "$.customers[*]",
            JoinKeyPath = "$.customerId",
            ItemPath = "$.customerDetails",
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("New York", dataContext.Get<string>("$.orders[0].customerDetails[0].city"));
        Assert.Equal("Chicago", dataContext.Get<string>("$.orders[1].customerDetails[0].city"));
        Assert.Equal("Seattle", dataContext.Get<string>("$.orders[2].customerDetails[0].city"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyJoinArray_NoMatchHandlingFail_ThrowsException()
    {
        // An empty lookup under Fail is reported as such (join path, no key) instead of as a
        // mismatch of the first key: an absent or unpopulated join array is a different problem
        // than a stray key. Ignore keeps the empty-array shortcut
        // (see ProcessObjectAsync_EmptyJoinArray_SetsEmptyArrays).
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[] { new { orderId = "123", customerName = "John Doe" } },
            orderItems = Array.Empty<object>()
        });
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));

        Assert.Contains("'$.orderItems[*]'", exception.Message);
        Assert.Contains("no records", exception.Message);
        Assert.DoesNotContain("Join key", exception.Message);
        A.CallTo(() => fn.Invoke(A<IDataContext>._, A<INodeContext>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessObjectAsync_EmptyJoinArray_MissingKey_NoMatchHandlingFail_ThrowsException(bool allowMissingKey)
    {
        // Under Fail an empty lookup fails at the first source object whatever its key, so the
        // keyless outcome does not depend on allowMissingKey here and no key is read.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            AllowMissingKey = allowMissingKey,
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[] { new { customerName = "John Doe" } },
            orderItems = Array.Empty<object>()
        });
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        var exception = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => testee.ProcessObjectAsync(dataContext, nodeContext));

        Assert.Contains("no records", exception.Message);
        Assert.DoesNotContain("Value not set", exception.Message);
        A.CallTo(() => fn.Invoke(A<IDataContext>._, A<INodeContext>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyJoinArray_MissingKey_Defaults_SetsEmptyArrays()
    {
        // Characterization of pre-existing behavior kept on purpose: with an empty lookup and
        // noMatchHandling Ignore the node writes empty arrays without reading the source key, so
        // a missing key does not throw here even though allowMissingKey is off.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items"
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[] { new { customerName = "John Doe" } },
            orderItems = Array.Empty<object>()
        });
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.orders[0].items"));
        Assert.Equal(0, dataContext.Length("$.orders[0].items"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MissingKey_AllowMissingKey_NoMatchHandlingFail_SetsEmptyArray()
    {
        // allowMissingKey decides the "no key" case, noMatchHandling the "key without match"
        // case: with a non-empty lookup a source object without a key is a left-join row even
        // under Fail.
        JoinNodeConfiguration joinNodeConfiguration = new()
        {
            Path = "$.orders[*]",
            KeyPath = "$.orderId",
            JoinPath = "$.orderItems[*]",
            JoinKeyPath = "$.orderId",
            ItemPath = "$.items",
            AllowMissingKey = true,
            NoMatchHandling = JoinNoMatchHandlingDto.Fail
        };

        var (dataContext, nodeContext) = PrepareTest(joinNodeConfiguration, new
        {
            orders = new[]
            {
                new { orderId = (string?)null, customerName = "John Doe" },
                new { orderId = (string?)"123", customerName = "Jane Smith" }
            },
            orderItems = new[]
            {
                new { orderId = (string?)"123", productName = "Widget" }
            }
        });
        var fn = A.Fake<NodeDelegate>();
        var testee = new JoinNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.orders[0].items"));
        Assert.Equal(0, dataContext.Length("$.orders[0].items"));
        Assert.Equal(1, dataContext.Length("$.orders[1].items"));
    }
}
