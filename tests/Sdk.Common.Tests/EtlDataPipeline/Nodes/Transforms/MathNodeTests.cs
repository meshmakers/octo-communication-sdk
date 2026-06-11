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

public class MathNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(MathNodeConfiguration mathNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new[]
            {
                new { value = 10.0, multiplier = 2.0 },
                new { value = 20.0, multiplier = 3.0 },
                new { value = 5.5, multiplier = 1.5 }
            },
            globalMultiplier = 4.0,
            singleValue = 15.0
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    private (IDataContext, INodeContext) PrepareRoundingTest(MathNodeConfiguration mathNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            values = new[]
            {
                new { amount = 3.14159 },
                new { amount = 2.67891 },
                new { amount = 10.999 },
                new { amount = 123.456789 },
                new { amount = 0.12345 }
            },
            singleAmount = 15.6789
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_Add_WithValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 5.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(15.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(25.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(10.5, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Subtract_WithValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Subtract,
            Value = 3.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(7.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(17.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(2.5, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Multiply_WithValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Multiply,
            Value = 2.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(20.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(40.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(11.0, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Divide_WithValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Divide,
            Value = 2.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(5.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(10.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(2.75, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Add_WithValuePath_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            ValuePath = "$.globalMultiplier"
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(14.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(24.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(9.5, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Multiply_WithValuePath_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Multiply,
            ValuePath = "$.globalMultiplier"
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(40.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(80.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(22.0, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SingleValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$",
            ItemPath = "$.singleValue",
            ItemTargetPath = "$.calculatedValue",
            Operation = MathOperationDto.Multiply,
            Value = 3.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(45.0, dataContext.Get<double>("$.calculatedValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullInputValue_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("null"));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 5.0
        };

        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoSourceData_Warning()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.nonexistent[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 5.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_NoValue_ThrowsException()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            ValuePath = "$.nonexistent"
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoNumericValueAtItemPath_Warning()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new object[]
            {
                new { value = (double?)null },
                new { value = (double?)20.0 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 5.0
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.False(dataContext.Exists("$.items[0].result"));
        Assert.Equal(25.0, dataContext.Get<double>("$.items[1].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NumericStringValue_ParsedAndComputed()
    {
        // Characterizes the numeric-string read path (a JSON string that holds a number):
        // it is parsed as a double under invariant culture and the operation is applied.
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new object[]
            {
                new { value = "10.5" },
                new { value = "20" }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 5.0
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(15.5, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(25.0, dataContext.Get<double>("$.items[1].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NonNumericStringValue_Skipped()
    {
        // Characterizes the non-numeric read path: a string that is not a number is
        // skipped (warning), and processing continues with the remaining items.
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new object[]
            {
                new { value = "abc" },
                new { value = 20.0 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 5.0
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.False(dataContext.Exists("$.items[0].result"));
        Assert.Equal(25.0, dataContext.Get<double>("$.items[1].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DivideByZero_ReturnsInfinity()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[0]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Divide,
            Value = 0.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.True(double.IsInfinity(dataContext.Get<double>("$.items[0].result")));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnsupportedOperation_ThrowsException()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[0]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = (MathOperationDto)999,
            Value = 5.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await Assert.ThrowsAsync<NotSupportedException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NegativeNumbers_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new[]
            {
                new { value = -10.0 },
                new { value = -5.5 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Multiply,
            Value = -2.0
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(20.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(11.0, dataContext.Get<double>("$.items[1].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DecimalPrecision_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new[]
            {
                new { value = 0.1 },
                new { value = 0.2 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Add,
            Value = 0.3
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(0.4, dataContext.Get<double>("$.items[0].result"), 1);
        Assert.Equal(0.5, dataContext.Get<double>("$.items[1].result"), 1);
    }

    [Fact]
    public async Task ProcessObjectAsync_Modulo_WithValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Modulo,
            Value = 3.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(1.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(2.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(2.5, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Modulo_WithValuePath_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Modulo,
            ValuePath = "$.globalMultiplier"
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(2.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(0.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(1.5, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Modulo_WithZero_ReturnsNaN()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[0]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Modulo,
            Value = 0.0
        };

        var (dataContext, nodeContext) = PrepareTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.True(double.IsNaN(dataContext.Get<double>("$.items[0].result")));
    }

    [Fact]
    public async Task ProcessObjectAsync_Modulo_WithNegativeNumbers_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new[]
            {
                new { value = -10.0 },
                new { value = -7.0 },
                new { value = 13.0 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.items[*]",
            ItemPath = "$.value",
            ItemTargetPath = "$.result",
            Operation = MathOperationDto.Modulo,
            Value = 3.0
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(-1.0, dataContext.Get<double>("$.items[0].result"));
        Assert.Equal(-1.0, dataContext.Get<double>("$.items[1].result"));
        Assert.Equal(1.0, dataContext.Get<double>("$.items[2].result"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_ToInteger_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.values[*]",
            ItemPath = "$.amount",
            ItemTargetPath = "$.rounded",
            Operation = MathOperationDto.Round,
            DecimalPlaces = 0
        };

        var (dataContext, nodeContext) = PrepareRoundingTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.0, dataContext.Get<double>("$.values[0].rounded"));
        Assert.Equal(3.0, dataContext.Get<double>("$.values[1].rounded"));
        Assert.Equal(11.0, dataContext.Get<double>("$.values[2].rounded"));
        Assert.Equal(123.0, dataContext.Get<double>("$.values[3].rounded"));
        Assert.Equal(0.0, dataContext.Get<double>("$.values[4].rounded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_ToTwoDecimalPlaces_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.values[*]",
            ItemPath = "$.amount",
            ItemTargetPath = "$.rounded",
            Operation = MathOperationDto.Round,
            DecimalPlaces = 2
        };

        var (dataContext, nodeContext) = PrepareRoundingTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.14, dataContext.Get<double>("$.values[0].rounded"));
        Assert.Equal(2.68, dataContext.Get<double>("$.values[1].rounded"));
        Assert.Equal(11.0, dataContext.Get<double>("$.values[2].rounded"));
        Assert.Equal(123.46, dataContext.Get<double>("$.values[3].rounded"));
        Assert.Equal(0.12, dataContext.Get<double>("$.values[4].rounded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_ToFourDecimalPlaces_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.values[*]",
            ItemPath = "$.amount",
            ItemTargetPath = "$.rounded",
            Operation = MathOperationDto.Round,
            DecimalPlaces = 4
        };

        var (dataContext, nodeContext) = PrepareRoundingTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.1416, dataContext.Get<double>("$.values[0].rounded"));
        Assert.Equal(2.6789, dataContext.Get<double>("$.values[1].rounded"));
        Assert.Equal(10.999, dataContext.Get<double>("$.values[2].rounded"));
        Assert.Equal(123.4568, dataContext.Get<double>("$.values[3].rounded"));
        Assert.Equal(0.1234, dataContext.Get<double>("$.values[4].rounded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_SingleValue_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$",
            ItemPath = "$.singleAmount",
            ItemTargetPath = "$.roundedAmount",
            Operation = MathOperationDto.Round,
            DecimalPlaces = 1
        };

        var (dataContext, nodeContext) = PrepareRoundingTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(15.7, dataContext.Get<double>("$.roundedAmount"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_NegativeNumbers_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            negativeValues = new[]
            {
                new { amount = -3.14159 },
                new { amount = -2.67891 },
                new { amount = -0.5555 }
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.negativeValues[*]",
            ItemPath = "$.amount",
            ItemTargetPath = "$.rounded",
            Operation = MathOperationDto.Round,
            DecimalPlaces = 2
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Math", 0, mathNodeConfiguration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(-3.14, dataContext.Get<double>("$.negativeValues[0].rounded"));
        Assert.Equal(-2.68, dataContext.Get<double>("$.negativeValues[1].rounded"));
        Assert.Equal(-0.56, dataContext.Get<double>("$.negativeValues[2].rounded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_ZeroDecimalPlaces_DefaultBehavior()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.values[0]",
            ItemPath = "$.amount",
            ItemTargetPath = "$.rounded",
            Operation = MathOperationDto.Round
        };

        var (dataContext, nodeContext) = PrepareRoundingTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.0, dataContext.Get<double>("$.values[0].rounded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Round_ExcessiveDecimalPlaces_OK()
    {
        MathNodeConfiguration mathNodeConfiguration = new()
        {
            Path = "$.values[0]",
            ItemPath = "$.amount",
            ItemTargetPath = "$.rounded",
            Operation = MathOperationDto.Round,
            DecimalPlaces = 10
        };

        var (dataContext, nodeContext) = PrepareRoundingTest(mathNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new MathNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(3.14159, dataContext.Get<double>("$.values[0].rounded"));
    }
}
