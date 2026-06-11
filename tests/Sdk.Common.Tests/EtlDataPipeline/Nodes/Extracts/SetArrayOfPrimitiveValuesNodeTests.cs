using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Extracts;

public class SetArrayOfPrimitiveValuesNodeTests(ServiceCollectionFixture fixture)
    : IClassFixture<ServiceCollectionFixture>
{
    private static IDataContext CreateContext(string json = "{}") => new DataContextImpl(JsonDocument.Parse(json));

    [Fact]
    public async Task ProcessObjectAsync_WithIntegerArray_SetsValues()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.numbers",
                Values = new object[] { 1, 2, 3, 4, 5 }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.numbers"));
        Assert.Equal(5, dataContext.Length("$.numbers"));
        Assert.Equal(1, dataContext.Get<int>("$.numbers[0]"));
        Assert.Equal(5, dataContext.Get<int>("$.numbers[4]"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithStringArray_SetsValues()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.names",
                Values = new object[] { "Alice", "Bob", "Charlie" }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.names"));
        Assert.Equal("Alice", dataContext.Get<string>("$.names[0]"));
        Assert.Equal("Charlie", dataContext.Get<string>("$.names[2]"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithMixedArray_SetsValues()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.mixed",
                Values = new object[] { 1, "two", 3.0, true }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(4, dataContext.Length("$.mixed"));
        Assert.Equal(1, dataContext.Get<int>("$.mixed[0]"));
        Assert.Equal("two", dataContext.Get<string>("$.mixed[1]"));
        Assert.Equal(3.0, dataContext.Get<double>("$.mixed[2]"));
        Assert.True(dataContext.Get<bool>("$.mixed[3]"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithEmptyArray_SetsEmptyArray()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.empty",
                Values = Array.Empty<object>()
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Array, dataContext.GetKind("$.empty"));
        Assert.Equal(0, dataContext.Length("$.empty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithDoubleArray_SetsValues()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.decimals",
                Values = new object[] { 1.1, 2.2, 3.3 }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.decimals"));
        Assert.Equal(1.1, dataContext.Get<double>("$.decimals[0]"));
    }

    [Fact]
    public async Task ProcessObjectAsync_OverwritesExistingValue()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext("{\"data\":[100,200]}");

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.data",
                Values = new object[] { 1, 2, 3 }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.data"));
        Assert.Equal(1, dataContext.Get<int>("$.data[0]"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithNestedPath_SetsValues()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext("{\"nested\":{}}");

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.nested.values",
                Values = new object[] { "a", "b", "c" }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.nested.values"));
    }

    [Fact]
    public async Task ProcessObjectAsync_WithBooleanArray_SetsValues()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = CreateContext();

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("SetArrayOfPrimitiveValues", 0,
            new SetArrayOfPrimitiveValuesNodeConfiguration
            {
                TargetPath = "$.flags",
                Values = new object[] { true, false, true }
            }, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SetArrayOfPrimitiveValuesNode(fn);
        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3, dataContext.Length("$.flags"));
        Assert.True(dataContext.Get<bool>("$.flags[0]"));
        Assert.False(dataContext.Get<bool>("$.flags[1]"));
    }
}
