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

public class SwitchNodeTests(NodeFixture fixture)
    : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(SwitchNodeConfiguration switchNodeConfiguration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            StringValue = "option1",
            IntValue = 42,
            BoolValue = true,
            DoubleValue = 3.14,
            EnumValue = 1
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Switch", 0, switchNodeConfiguration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_MatchingCase_ExecutesCorrectTransformations()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.StringValue",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = "option1",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.Result1" }
                    }
                },
                new()
                {
                    Value = "option2",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.Result2" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(100);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(100, dataContext.Get<int>("$.Result1"));
        Assert.False(dataContext.Exists("$.Result2"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoMatchingCase_ExecutesDefaultTransformations()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.StringValue",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = "option2",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.CaseResult" }
                    }
                }
            },
            Default = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.DefaultResult" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(200);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(200, dataContext.Get<int>("$.DefaultResult"));
        Assert.False(dataContext.Exists("$.CaseResult"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoMatchingCaseAndNoDefault_DoesNothing()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.StringValue",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = "option2",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.CaseResult" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustNotHaveHappened();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.False(dataContext.Exists("$.CaseResult"));
    }

    [Fact]
    public async Task ProcessObjectAsync_IntegerCase_MatchesCorrectly()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.IntValue",
            ValueType = AttributeValueTypesDto.Int,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = 42,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.IntResult" }
                    }
                },
                new()
                {
                    Value = 99,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.OtherResult" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(300);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(300, dataContext.Get<int>("$.IntResult"));
        Assert.False(dataContext.Exists("$.OtherResult"));
    }

    [Fact]
    public async Task ProcessObjectAsync_BooleanCase_MatchesCorrectly()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.BoolValue",
            ValueType = AttributeValueTypesDto.Boolean,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = true,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.TrueResult" }
                    }
                },
                new()
                {
                    Value = false,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.FalseResult" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(400);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(400, dataContext.Get<int>("$.TrueResult"));
        Assert.False(dataContext.Exists("$.FalseResult"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DoubleCase_MatchesCorrectly()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.DoubleValue",
            ValueType = AttributeValueTypesDto.Double,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = 3.14,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.PiResult" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(500);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(500, dataContext.Get<int>("$.PiResult"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MultipleCasesWithSameValue_MatchesFirst()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.StringValue",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = "option1",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.FirstMatch" }
                    }
                },
                new()
                {
                    Value = "option1",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.SecondMatch" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(600);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedOnceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(600, dataContext.Get<int>("$.FirstMatch"));
        Assert.False(dataContext.Exists("$.SecondMatch"));
    }

    [Fact]
    public async Task SwitchNode_MissingPath_BooleanFalseCase_Matches_NewtonsoftParity()
    {
        // Newtonsoft parity (reverses the temporary be7a19f "IfNode parity" tweak): the legacy
        // SwitchNode read booleans via the NON-nullable JToken.GetSimpleValueByPath<bool>, which
        // returns default(bool)=false for an absent path. So a {Value:false} case MUST match a
        // missing Boolean path, and the Default branch must NOT run. (IfNode legitimately differs:
        // it used the nullable overload — null on miss.)
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.flag",
            ValueType = AttributeValueTypesDto.Boolean,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = false,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.CaseFalseRan" }
                    }
                }
            },
            Default = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.DefaultRan" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(700);

        // Empty document — $.flag is missing.
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Switch", 0, switchNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        // The Boolean false case MUST execute against a missing path (false == default(bool)).
        Assert.Equal(700, dataContext.Get<int>("$.CaseFalseRan"));
        // The Default branch must NOT have executed.
        Assert.False(dataContext.Exists("$.DefaultRan"));
    }

    [Fact]
    public async Task SwitchNode_MissingPath_IntZeroCase_Matches_NewtonsoftParity()
    {
        // A missing Int path reads as default(int)=0 (legacy GetSimpleValueByPath<int>), so a
        // {Value:0} case matches and the Default branch must NOT run.
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.count",
            ValueType = AttributeValueTypesDto.Int,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = 0,
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.CaseZeroRan" }
                    }
                }
            },
            Default = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.DefaultRan" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(800);

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Switch", 0, switchNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(800, dataContext.Get<int>("$.CaseZeroRan"));
        Assert.False(dataContext.Exists("$.DefaultRan"));
    }

    [Fact]
    public async Task SwitchNode_MissingPath_String_FallsToDefault()
    {
        // String is a reference type: a missing String path reads as null (default(string)),
        // which matches no case (case Values are required/non-null), so the Default branch runs.
        // This half is unchanged by the nullable→non-nullable switch and is pinned for contrast.
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.name",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = "expected",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.CaseRan" }
                    }
                }
            },
            Default = new List<NodeConfiguration>
            {
                new TestNodeConfiguration { TargetPath = "$.DefaultRan" }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        A.CallTo(() => testCounter.GetNext()).Returns(900);

        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("{}"));
        var rootNodeContext =
            NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Switch", 0, switchNodeConfiguration, dataContext);

        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.False(dataContext.Exists("$.CaseRan"));
        Assert.Equal(900, dataContext.Get<int>("$.DefaultRan"));
    }

    [Fact]
    public async Task ProcessObjectAsync_MultipleTransformationsInCase_ExecutesAll()
    {
        var switchNodeConfiguration = new SwitchNodeConfiguration
        {
            Path = "$.StringValue",
            ValueType = AttributeValueTypesDto.String,
            Cases = new List<SwitchCase>
            {
                new()
                {
                    Value = "option1",
                    Transformations = new List<NodeConfiguration>
                    {
                        new TestNodeConfiguration { TargetPath = "$.Result1" },
                        new TestNodeConfiguration { TargetPath = "$.Result2" }
                    }
                }
            }
        };

        var testCounter = A.Fake<ITestCounter>();
        fixture.Services.AddSingleton(testCounter);
        var callCount = 0;
        A.CallTo(() => testCounter.GetNext()).ReturnsLazily(() => ++callCount * 100);

        var (dataContext, nodeContext) = PrepareTest(switchNodeConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new SwitchNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => testCounter.GetNext()).MustHaveHappenedTwiceExactly();
        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(100, dataContext.Get<int>("$.Result1"));
        Assert.Equal(200, dataContext.Get<int>("$.Result2"));
    }
}
