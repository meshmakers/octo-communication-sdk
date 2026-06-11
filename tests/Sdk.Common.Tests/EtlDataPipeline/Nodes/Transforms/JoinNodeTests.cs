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
    private (IDataContext, INodeContext) PrepareTest(JoinNodeConfiguration joinNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
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
}
