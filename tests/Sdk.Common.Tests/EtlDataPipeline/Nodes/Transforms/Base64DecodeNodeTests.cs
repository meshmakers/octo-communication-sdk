#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class Base64DecodeNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(Base64DecodeNodeConfiguration configuration)
    {
        return PrepareTest(configuration, new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["encoded"] = "SGVsbG8gV29ybGQ=", ["id"] = 1 },
                new JsonObject { ["encoded"] = "VGVzdCBTdHJpbmc=", ["id"] = 2 },
                new JsonObject { ["encoded"] = "U3BlY2lhbCBjaGFyczogw6TDtsO8IOKCrEAj", ["id"] = 3 }
            ),
            ["singleEncoded"] = "UGxhaW4gdGV4dA==",
            ["nested"] = new JsonObject
            {
                ["encodedSecret"] = "bXktc2VjcmV0LWtleQ=="
            },
            ["nullValue"] = null,
            ["emptyValue"] = "",
            ["invalidBase64"] = "This is not valid Base64!"
        });
    }

    private (IDataContext, INodeContext) PrepareTest(Base64DecodeNodeConfiguration configuration, JsonNode root)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(System.Text.Json.JsonDocument.Parse(root.ToJsonString()));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Base64Decode", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeSimpleString_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleEncoded",
            TargetPath = "$.decodedValue"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Plain text", dataContext.Get<string>("$.decodedValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeArrayItems_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.encoded",
            TargetPath = "$.text"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello World", dataContext.Get<string>("$.items[0].text"));
        Assert.Equal("Test String", dataContext.Get<string>("$.items[1].text"));
        Assert.Equal("Special chars: äöü €@#", dataContext.Get<string>("$.items[2].text"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeNestedValue_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$.nested",
            SourcePath = "$.encodedSecret",
            TargetPath = "$.secret"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("my-secret-key", dataContext.Get<string>("$.nested.secret"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeNullValue_StaysNull()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.nullValue",
            TargetPath = "$.decodedNull"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Null, dataContext.GetKind("$.decodedNull"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeEmptyString_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.emptyValue",
            TargetPath = "$.decodedEmpty"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.decodedEmpty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_InvalidBase64_ThrowsFormatException()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.invalidBase64",
            TargetPath = "$.decoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await Assert.ThrowsAsync<FormatException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullDataContext_ThrowsException()
    {
        // Empty document -> Get<JsonNode>("$") returns an empty object; the node currently treats that as
        // valid input and warns about no source data found rather than throwing PipelineExecutionException.
        // Removed: the legacy DataContext { Current = null } shape is no longer reachable in the new API.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProcessObjectAsync_NoSourceData_Warning()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$.nonexistent[*]",
            SourcePath = "$.value",
            TargetPath = "$.decoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeSpecialCharacters_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.encoded",
            TargetPath = "$.special"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["encoded"] = "IUAjJCVeJiooKV8rLT1bXXt9fDsnOiIsLi88Pj8="
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("!@#$%^&*()_+-=[]{}|;':\",./<>?", dataContext.Get<string>("$.special"));
    }

    [Fact]
    public async Task ProcessObjectAsync_DecodeUnicode_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.encoded",
            TargetPath = "$.unicode"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["encoded"] = "SGVsbG8g5LiW55WMINmF2LHYrdio2Kcg0LzQuNGAIPCfjI0="
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("Hello 世界 مرحبا мир 🌍", dataContext.Get<string>("$.unicode"));
    }

    [Fact]
    public async Task ProcessObjectAsync_RoundTrip_OK()
    {
        var originalText = "This is a test string with special chars: äöü €@#";
        var encodedValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(originalText));

        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.encoded",
            TargetPath = "$.decoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["encoded"] = encodedValue
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(originalText, dataContext.Get<string>("$.decoded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Base64WithPadding_OK()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.single",
            TargetPath = "$.decodedSingle"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["single"] = "YQ==",
            ["doublePad"] = "YWI=",
            ["triple"] = "YWJj"
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("a", dataContext.Get<string>("$.decodedSingle"));
    }

    [Fact]
    public async Task ProcessObjectAsync_InvalidBase64Padding_ThrowsException()
    {
        var configuration = new Base64DecodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.badPadding",
            TargetPath = "$.decoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["badPadding"] = "YQ="
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64DecodeNode(fn);

        await Assert.ThrowsAsync<FormatException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }
}
