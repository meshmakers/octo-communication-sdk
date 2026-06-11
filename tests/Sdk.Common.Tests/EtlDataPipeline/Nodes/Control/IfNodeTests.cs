using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;
using Sdk.Common.Tests.TestData;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Control;

public class IfNodeTests(NodeFixture fixture)
    : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(IfNodeConfiguration ifNodeConfiguration, object? testData = null)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = testData ?? new
        {
            StringValue = "test",
            BoolValue = true,
            IntValue = 10,
            DoubleValue = 10.5,
            DateValue = DateTime.Now,
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("If", 0, ifNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_Equals_Condition_True_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "test",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.Equal,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Equals_Condition_False_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "es",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.Equal,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_NotEquals_Condition_True_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "not_matching",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.NotEqual,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Contains_Condition_True_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "es",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.Contains,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ValuePath_Instead_Of_Value_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            ValuePath = "$.StringValue",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.Equal,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_StartsWith_Condition_True_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "te",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.StartsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_StartsWith_Condition_False_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "st",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.StartsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_StartsWith_CaseInsensitive_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "TE",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.StartsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EndsWith_Condition_True_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "st",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.EndsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EndsWith_Condition_False_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "te",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.EndsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_EndsWith_CaseInsensitive_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "ST",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.EndsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_RegexMatch_Condition_True_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "^te.*",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.RegexMatch,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_RegexMatch_Condition_False_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "^st.*",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.RegexMatch,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProcessObjectAsync_RegexMatch_ComplexPattern_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = @"^t\w{3}$",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.RegexMatch,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1, dataContext.Get<int>("$.Result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_StartsWith_NullValues_OK()
    {
        IfNodeConfiguration ifNodeConfiguration = new()
        {
            Path = "$.StringValue",
            Value = "te",
            ValueType = AttributeValueTypesDto.String,
            Operator = CompareOperator.StartsWith,
            Transformations = new List<NodeConfiguration>
            {
                new TestNodeConfiguration
                {
                    TargetPath = "$.Result"
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(1);

        var testData = new
        {
            StringValue = (string?)null,
            BoolValue = true,
            IntValue = 10,
            DoubleValue = 10.5,
            DateValue = DateTime.Now,
        };
        var (dataContext, nodeContext) = PrepareTest(ifNodeConfiguration, testData);
        var fn = A.Fake<NodeDelegate>();
        var testee = new IfNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
    }
}
