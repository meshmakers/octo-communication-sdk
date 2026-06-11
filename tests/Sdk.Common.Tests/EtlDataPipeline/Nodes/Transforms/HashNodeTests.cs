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

public class HashNodeTests(NodeFixture fixture) : IClassFixture<NodeFixture>
{
    private (IDataContext, INodeContext) PrepareTest(HashNodeConfiguration configuration)
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            items = new[]
            {
                new { text = "Hello World", password = "secret123", id = 1 },
                new { text = "Test String", password = "mypassword", id = 2 },
                new { text = "Special chars: äöü €@#", password = "unicode_pwd", id = 3 }
            },
            singleValue = "Test Content",
            base64Value = "SGVsbG8gV29ybGQ=", // "Hello World" in Base64
            emptyValue = "",
            nullValue = (string?)null,
            binaryData = "VGhpcyBpcyBhIHRlc3Q=" // "This is a test" in Base64
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Hash", 0, configuration, dataContext);
        return (dataContext, nodeContext);
    }

    [Fact]
    public async Task ProcessObjectAsync_MD5_String_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.md5Hash",
            Algorithm = HashAlgorithmDto.Md5,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("d65cdbadce081581e7de64a5a44b4617", dataContext.Get<string>("$.md5Hash"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SHA1_String_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.sha1Hash",
            Algorithm = HashAlgorithmDto.Sha1,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("bebfefe6bd0a8175e99a83f217ed3d2dbfe55bc8", dataContext.Get<string>("$.sha1Hash"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SHA256_String_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.sha256Hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("60c9b75f15144a088fd7800e1049c6c80a92e76de588c2b21b30ff42f6694ce2", dataContext.Get<string>("$.sha256Hash"));
    }

    [Fact]
    public async Task ProcessObjectAsync_SHA384_String_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.sha384Hash",
            Algorithm = HashAlgorithmDto.Sha384,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var actualHash = dataContext.Get<string>("$.sha384Hash");
        Assert.NotNull(actualHash);
        Assert.Equal(96, actualHash.Length);
        Assert.Matches("^[0-9a-f]{96}$", actualHash);
    }

    [Fact]
    public async Task ProcessObjectAsync_SHA512_String_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.sha512Hash",
            Algorithm = HashAlgorithmDto.Sha512,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var actualHash = dataContext.Get<string>("$.sha512Hash");
        Assert.NotNull(actualHash);
        Assert.Equal(128, actualHash.Length);
        Assert.Matches("^[0-9a-f]{128}$", actualHash);
    }

    [Fact]
    public async Task ProcessObjectAsync_SHA256_Base64_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.base64Value",
            TargetPath = "$.base64Hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.Base64
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("a591a6d40bf420404a011733cfb7b190d62c65bf0bcda32b57b277d9ad9f146e", dataContext.Get<string>("$.base64Hash"));
    }

    [Fact]
    public async Task ProcessObjectAsync_ArrayItems_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$.items[*]",
            SourcePath = "$.text",
            TargetPath = "$.textHash",
            Algorithm = HashAlgorithmDto.Md5,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("b10a8db164e0754105b7a99be72e3fe5", dataContext.Get<string>("$.items[0].textHash"));
        Assert.Equal("bd08ba3c982eaad768602536fb8e1184", dataContext.Get<string>("$.items[1].textHash"));
        var thirdHash = dataContext.Get<string>("$.items[2].textHash");
        Assert.NotNull(thirdHash);
        Assert.Equal(32, thirdHash.Length);
        Assert.Matches("^[0-9a-f]{32}$", thirdHash);
    }

    [Fact]
    public async Task ProcessObjectAsync_EmptyString_OK()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.emptyValue",
            TargetPath = "$.emptyHash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", dataContext.Get<string>("$.emptyHash"));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullValue_ThrowsException()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.nullValue",
            TargetPath = "$.nullHash",
            Algorithm = HashAlgorithmDto.Md5,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NullDataContext_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var dataContext = new DataContextImpl(JsonDocument.Parse("null"));
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.value",
            TargetPath = "$.hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };
        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Hash", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_NoSourceData_Warning()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$.nonexistent[*]",
            SourcePath = "$.value",
            TargetPath = "$.hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ProcessObjectAsync_InvalidBase64_ThrowsException()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            invalidBase64 = "This is not valid Base64!"
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.invalidBase64",
            TargetPath = "$.hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.Base64
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Hash", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnsupportedAlgorithm_ThrowsException()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.hash",
            Algorithm = (HashAlgorithmDto)999,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnsupportedInputFormat_ThrowsException()
    {
        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = (HashInputFormatDto)999
        };

        var (dataContext, nodeContext) = PrepareTest(configuration);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await Assert.ThrowsAsync<PipelineExecutionException>(() => testee.ProcessObjectAsync(dataContext, nodeContext));
    }

    [Fact]
    public async Task ProcessObjectAsync_UnicodeCharacters_OK()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            unicode = "Hello 世界 مرحبا мир 🌍"
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.unicode",
            TargetPath = "$.unicodeHash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Hash", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        A.CallTo(() => fn.Invoke(dataContext, nodeContext)).MustHaveHappenedOnceExactly();
        var hash = dataContext.Get<string>("$.unicodeHash");
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public async Task ProcessObjectAsync_ConsistentHashing_OK()
    {
        var configuration1 = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.hash1",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var configuration2 = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.singleValue",
            TargetPath = "$.hash2",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var (dataContext, nodeContext1) = PrepareTest(configuration1);
        var (_, nodeContext2) = PrepareTest(configuration2);
        var fn = A.Fake<NodeDelegate>();
        var testee1 = new HashNode(fn);
        var testee2 = new HashNode(fn);

        await testee1.ProcessObjectAsync(dataContext, nodeContext1);
        await testee2.ProcessObjectAsync(dataContext, nodeContext2);

        var hash1 = dataContext.Get<string>("$.hash1");
        var hash2 = dataContext.Get<string>("$.hash2");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task ProcessObjectAsync_DifferentInputFormats_DifferentResults()
    {
        var logger = A.Fake<IPipelineLogger>();
        var seed = new
        {
            stringValue = "SGVsbG8gV29ybGQ=",
            base64Value = "SGVsbG8gV29ybGQ="
        };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var stringConfig = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.stringValue",
            TargetPath = "$.stringHash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var base64Config = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.base64Value",
            TargetPath = "$.base64Hash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.Base64
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext1 = rootNodeContext.RegisterChildNode("Hash", 0, stringConfig, dataContext);
        var nodeContext2 = rootNodeContext.RegisterChildNode("Hash", 1, base64Config, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext1);
        await testee.ProcessObjectAsync(dataContext, nodeContext2);

        var stringHash = dataContext.Get<string>("$.stringHash");
        var base64Hash = dataContext.Get<string>("$.base64Hash");
        Assert.NotEqual(stringHash, base64Hash);
    }

    [Fact]
    public async Task HashNode_BooleanValue_HashesCapitalizedForm()
    {
        // Hash-stability critical: pre-fix JsonNode.ToJsonString() rendered
        // booleans as lowercase "true"/"false". Pre-migration JToken.ToString
        // (Newtonsoft) rendered them capitalized ("True"/"False"). The
        // divergence silently broke any pipeline hashing a stringified boolean.
        // Post-fix HashNode routes through JsonStringifyHelper.ToLegacyString,
        // restoring the legacy capitalized form.
        //
        // SHA-256("True") = 3cbc87c7681f34db4617feaa2c8801931bc5e42d8d0f560e756dd4cd92885f18
        // SHA-256("true") = b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b
        const string expectedTrueHash = "3cbc87c7681f34db4617feaa2c8801931bc5e42d8d0f560e756dd4cd92885f18";
        const string lowercaseTrueHash = "b5bea41b6c623f7c09f1bf24dcae58ebab3c0cdd90ad966bc43a45b44867e12b";

        var logger = A.Fake<IPipelineLogger>();
        var seed = new { flag = true };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var configuration = new HashNodeConfiguration
        {
            Path = "$",
            SourcePath = "$.flag",
            TargetPath = "$.flagHash",
            Algorithm = HashAlgorithmDto.Sha256,
            InputFormat = HashInputFormatDto.String
        };

        var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
        var nodeContext = rootNodeContext.RegisterChildNode("Hash", 0, configuration, dataContext);
        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        await testee.ProcessObjectAsync(dataContext, nodeContext);

        var hash = dataContext.Get<string>("$.flagHash");
        Assert.Equal(expectedTrueHash, hash);
        Assert.NotEqual(lowercaseTrueHash, hash);
    }

    [Fact]
    public async Task ProcessObjectAsync_AllAlgorithmsProduceDifferentHashes_OK()
    {
        var algorithms = new[]
        {
            (HashAlgorithmDto.Md5, "md5", 32),
            (HashAlgorithmDto.Sha1, "sha1", 40),
            (HashAlgorithmDto.Sha256, "sha256", 64),
            (HashAlgorithmDto.Sha384, "sha384", 96),
            (HashAlgorithmDto.Sha512, "sha512", 128)
        };

        var logger = A.Fake<IPipelineLogger>();
        var seed = new { testValue = "Test Content" };
        var json = JsonSerializer.Serialize(seed, SystemTextJsonOptions.Default);
        var dataContext = new DataContextImpl(JsonDocument.Parse(json));

        var fn = A.Fake<NodeDelegate>();
        var testee = new HashNode(fn);

        var hashes = new List<string>();

        foreach (var (algorithm, targetPrefix, expectedLength) in algorithms)
        {
            var configuration = new HashNodeConfiguration
            {
                Path = "$",
                SourcePath = "$.testValue",
                TargetPath = $"$.{targetPrefix}Hash",
                Algorithm = algorithm,
                InputFormat = HashInputFormatDto.String
            };

            var rootNodeContext = NodeContext.CreateRootNodeContext(fixture.Services.BuildServiceProvider(), logger, dataContext);
            var nodeContext = rootNodeContext.RegisterChildNode("Hash", 0, configuration, dataContext);

            await testee.ProcessObjectAsync(dataContext, nodeContext);

            var hash = dataContext.Get<string>($"$.{targetPrefix}Hash");
            Assert.NotNull(hash);
            Assert.Equal(expectedLength, hash.Length);
            Assert.Matches("^[0-9a-f]+$", hash);
            hashes.Add(hash);
        }

        Assert.Equal(5, hashes.Distinct().Count());
    }
}
