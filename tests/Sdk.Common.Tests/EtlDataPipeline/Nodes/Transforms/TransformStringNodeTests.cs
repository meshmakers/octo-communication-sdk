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

public class TransformStringNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(TransformStringNodeConfiguration configuration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new[]
            {
                new { text = "  Hello World  ", name = "John Doe", email = "JOHN@EXAMPLE.COM" },
                new { text = "Test String", name = "  jane smith  ", email = "jane@EXAMPLE.com" },
                new { text = "Special chars: äöü €@#", name = "Bob Johnson", email = "BOB@test.COM" }
            },
            singleValue = "  Trim Me  ",
            longText = "This is a very long string for substring testing",
            emptyValue = "",
            nullValue = (string?)null
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("TransformString", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_Trim_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.text",
            TargetPath = "$.trimmed",
            Operation = StringOperationDto.Trim
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello World", dataContext.Get<string>("$.items[0].trimmed"));
        Assert.Equal("Test String", dataContext.Get<string>("$.items[1].trimmed"));
        Assert.Equal("Special chars: äöü €@#", dataContext.Get<string>("$.items[2].trimmed"));
    }

    [Fact]
    public async Task ProcessObjectAsync_TrimStart_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.name",
            TargetPath = "$.trimmedStart",
            Operation = StringOperationDto.TrimStart
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("John Doe", dataContext.Get<string>("$.items[0].trimmedStart"));
        Assert.Equal("jane smith  ", dataContext.Get<string>("$.items[1].trimmedStart"));
        Assert.Equal("Bob Johnson", dataContext.Get<string>("$.items[2].trimmedStart"));
    }

    [Fact]
    public async Task ProcessObjectAsync_TrimEnd_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.name",
            TargetPath = "$.trimmedEnd",
            Operation = StringOperationDto.TrimEnd
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("John Doe", dataContext.Get<string>("$.items[0].trimmedEnd"));
        Assert.Equal("  jane smith", dataContext.Get<string>("$.items[1].trimmedEnd"));
        Assert.Equal("Bob Johnson", dataContext.Get<string>("$.items[2].trimmedEnd"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ToUpper_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.name",
            TargetPath = "$.upperName",
            Operation = StringOperationDto.ToUpper
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("JOHN DOE", dataContext.Get<string>("$.items[0].upperName"));
        Assert.Equal("  JANE SMITH  ", dataContext.Get<string>("$.items[1].upperName"));
        Assert.Equal("BOB JOHNSON", dataContext.Get<string>("$.items[2].upperName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ToLower_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.email",
            TargetPath = "$.lowerEmail",
            Operation = StringOperationDto.ToLower
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("john@example.com", dataContext.Get<string>("$.items[0].lowerEmail"));
        Assert.Equal("jane@example.com", dataContext.Get<string>("$.items[1].lowerEmail"));
        Assert.Equal("bob@test.com", dataContext.Get<string>("$.items[2].lowerEmail"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromStart_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.longText",
            TargetPath = "$.prefix",
            Operation = StringOperationDto.SubstringFromStart,
            Length = 4
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("This", dataContext.Get<string>("$.prefix"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromEnd_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.longText",
            TargetPath = "$.suffix",
            Operation = StringOperationDto.SubstringFromEnd,
            Length = 7
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("testing", dataContext.Get<string>("$.suffix"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Substring_WithLength_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.longText",
            TargetPath = "$.middle",
            Operation = StringOperationDto.Substring,
            StartIndex = 5,
            Length = 4
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("is a", dataContext.Get<string>("$.middle"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Substring_WithoutLength_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.longText",
            TargetPath = "$.fromIndex",
            Operation = StringOperationDto.Substring,
            StartIndex = 40
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(" testing", dataContext.Get<string>("$.fromIndex"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SingleValue_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.trimmedSingle",
            Operation = StringOperationDto.Trim
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Trim Me", dataContext.Get<string>("$.trimmedSingle"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyString_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.emptyValue",
            TargetPath = "$.processedEmpty",
            Operation = StringOperationDto.ToUpper
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.processedEmpty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValue_StaysNull()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.nullValue",
            TargetPath = "$.processedNull",
            Operation = StringOperationDto.Trim
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Null, dataContext.GetKind("$.processedNull"));
        Assert.Null(dataContext.Get<string>("$.processedNull"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullDataContext_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("null"));
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.value",
            TargetPath = "$.processed",
            Operation = StringOperationDto.Trim
        };
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("TransformString", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoSourceData_Warning()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.nonexistent[*]",
            SourcePath = "$.value",
            TargetPath = "$.processed",
            Operation = StringOperationDto.Trim
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromStart_ExceedsLength_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.prefix",
            Operation = StringOperationDto.SubstringFromStart,
            Length = 100
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("John Doe", dataContext.Get<string>("$.items[0].prefix"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromEnd_ExceedsLength_OK()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.suffix",
            Operation = StringOperationDto.SubstringFromEnd,
            Length = 100
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("John Doe", dataContext.Get<string>("$.items[0].suffix"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Substring_OutOfBounds_ReturnsEmpty()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.outOfBounds",
            Operation = StringOperationDto.Substring,
            StartIndex = 100,
            Length = 5
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.items[0].outOfBounds"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Substring_NegativeIndex_ReturnsEmpty()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.negative",
            Operation = StringOperationDto.Substring,
            StartIndex = -5,
            Length = 3
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.items[0].negative"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromStart_MissingLength_ThrowsException()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.prefix",
            Operation = StringOperationDto.SubstringFromStart
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromEnd_MissingLength_ThrowsException()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.suffix",
            Operation = StringOperationDto.SubstringFromEnd
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnsupportedOperation_ThrowsException()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.result",
            Operation = (StringOperationDto)999
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await Assert.ThrowsAsync<NotSupportedException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_SubstringFromStart_ZeroLength_ReturnsEmpty()
    {
        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.items[0]",
            SourcePath = "$.name",
            TargetPath = "$.empty",
            Operation = StringOperationDto.SubstringFromStart,
            Length = 0
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.items[0].empty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_CombinedOperations_OK()
    {
        var trimConfiguration = new TransformStringNodeConfiguration
        {
            Path = "$.items[1]",
            SourcePath = "$.name",
            TargetPath = "$.trimmedName",
            Operation = StringOperationDto.Trim
        };

        var upperConfiguration = new TransformStringNodeConfiguration
        {
            Path = "$.items[1]",
            SourcePath = "$.trimmedName",
            TargetPath = "$.finalName",
            Operation = StringOperationDto.ToUpper
        };

        var (dataContext, nodeContext1) = PrepareTest(trimConfiguration);
        var (_, nodeContext2) = PrepareTest(upperConfiguration);
        var fn = A.Fake<NodeDelegate>();
        var trimNode = new TransformStringNode(fn);
        var upperNode = new TransformStringNode(fn);

        await trimNode.ProcessObjectAsync(dataContext, nodeContext1);
        await upperNode.ProcessObjectAsync(dataContext, nodeContext2);

        Assert.Equal("jane smith", dataContext.Get<string>("$.items[1].trimmedName"));
        Assert.Equal("JANE SMITH", dataContext.Get<string>("$.items[1].finalName"));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnicodeCharacters_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            unicode = new
            {
                text = "  Hello 世界 مرحبا мир 🌍  "
            }
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var configuration = new TransformStringNodeConfiguration
        {
            Path = "$.unicode",
            SourcePath = "$.text",
            TargetPath = "$.processed",
            Operation = StringOperationDto.Trim
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("TransformString", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new TransformStringNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello 世界 مرحبا мир 🌍", dataContext.Get<string>("$.unicode.processed"));
    }
}
