#pragma warning disable CS8602 // Dereference of a possibly null reference.

using System.Text.Json;
using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.Fixtures;

namespace Sdk.Common.Tests.EtlDataPipeline.Nodes.Transforms;

public class Base64EncodeNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(Base64EncodeNodeConfiguration configuration)
    {
        return PrepareTest(configuration, new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["text"] = "Hello World", ["id"] = 1 },
                new JsonObject { ["text"] = "Test String", ["id"] = 2 },
                new JsonObject { ["text"] = "Special chars: äöü €@#", ["id"] = 3 }
            ),
            ["singleValue"] = "Plain text",
            ["nested"] = new JsonObject
            {
                ["secret"] = "my-secret-key"
            },
            ["nullValue"] = null,
            ["emptyValue"] = ""
        });
    }

    private (IDataContext, INodeContext) PrepareTest(Base64EncodeNodeConfiguration configuration, JsonNode root)
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse(root.ToJsonString()));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Base64Encode", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_EncodeSimpleString_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.encodedValue"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("UGxhaW4gdGV4dA==", dataContext.Get<string>("$.encodedValue"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EncodeArrayItems_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.text",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("SGVsbG8gV29ybGQ=", dataContext.Get<string>("$.items[0].encoded"));
        Assert.Equal("VGVzdCBTdHJpbmc=", dataContext.Get<string>("$.items[1].encoded"));
        Assert.Equal("U3BlY2lhbCBjaGFyczogw6TDtsO8IOKCrEAj", dataContext.Get<string>("$.items[2].encoded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EncodeNestedValue_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$.nested",
            SourcePath = "$.secret",
            TargetPath = "$.encodedSecret"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("bXktc2VjcmV0LWtleQ==", dataContext.Get<string>("$.nested.encodedSecret"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EncodeNullValue_StaysNull()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.nullValue",
            TargetPath = "$.encodedNull"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal(DataKind.Null, dataContext.GetKind("$.encodedNull"));
    }

    [Fact]
    public async Task ProcessObjectAsync_EncodeEmptyString_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.emptyValue",
            TargetPath = "$.encodedEmpty"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("", dataContext.Get<string>("$.encodedEmpty"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoSourceData_Warning()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$.nonexistent[*]",
            SourcePath = "$.value",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_SpecialCharacters_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.special",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["special"] = "!@#$%^&*()_+-=[]{}|;':\",./<>?"
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("IUAjJCVeJiooKV8rLT1bXXt9fDsnOiIsLi88Pj8=", dataContext.Get<string>("$.encoded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_Unicode_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.unicode",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["unicode"] = "Hello 世界 مرحبا мир 🌍"
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("SGVsbG8g5LiW55WMINmF2LHYrdio2Kcg0LzQuNGAIPCfjI0=", dataContext.Get<string>("$.encoded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_OverwriteExisting_OK()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.value",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["value"] = "test",
            ["encoded"] = "old-value"
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("dGVzdA==", dataContext.Get<string>("$.encoded"));
    }

    [Fact]
    public async Task ProcessObjectAsync_LongString_OK()
    {
        var longString = new string('A', 1000);
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.longValue",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["longValue"] = longString
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var encoded = dataContext.Get<string>("$.encoded");
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        Assert.Equal(longString, decoded);
    }

    [Fact]
    public async Task Encode_ObjectSource_UsesIndentedNewtonsoftParityFormat()
    {
        // Sibling nodes (HashNode, ConcatNode, JoinNode) route through
        // JsonStringifyHelper.ToLegacyString which formats objects/arrays with
        // Newtonsoft's Formatting.Indented shape (2-space indent, "\n" newlines).
        // Base64EncodeNode used to emit compact JSON for objects, which diverged
        // and would change the ciphertext across the Newtonsoft→STJ migration
        // boundary for any caller comparing base64 outputs.
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.obj",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["obj"] = new JsonObject { ["a"] = 1, ["b"] = 2 }
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var encoded = dataContext.Get<string>("$.encoded");
        Assert.NotNull(encoded);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        // Must match Newtonsoft's Formatting.Indented output: 2-space indent, "\n" newlines
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n}", decoded);
    }

    [Fact]
    public async Task ProcessObjectAsync_DecimalNumbers_UsesInvariantCulture()
    {
        var configuration = new Base64EncodeNodeConfiguration
        {
            Path = "$.numbers[*]",
            SourcePath = "$.value",
            TargetPath = "$.encoded"
        };

        var (dataContext, nodeContext) = PrepareTest(configuration, new JsonObject
        {
            ["numbers"] = new JsonArray(
                new JsonObject { ["value"] = 217.21, ["description"] = "decimal with point" },
                new JsonObject { ["value"] = 1234.56789, ["description"] = "decimal with many digits" },
                new JsonObject { ["value"] = 42, ["description"] = "integer" }
            )
        });

        var fn = A.Fake<NodeDelegate>();
        var testee = new Base64EncodeNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();

        // Verify decimals are encoded using invariant culture (period separator)
        var encoded1 = dataContext.Get<string>("$.numbers[0].encoded");
        var decoded1 = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded1!));
        Assert.Equal("217.21", decoded1);
        Assert.Equal("MjE3LjIx", encoded1);

        var encoded2 = dataContext.Get<string>("$.numbers[1].encoded");
        var decoded2 = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded2!));
        Assert.Equal("1234.56789", decoded2);

        var encoded3 = dataContext.Get<string>("$.numbers[2].encoded");
        var decoded3 = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded3!));
        Assert.Equal("42", decoded3);
        Assert.Equal("NDI=", encoded3);
    }
}
