using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Internal;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;

/// <summary>
/// Represents a hash algorithm that can be used for calculating hash values.
/// </summary>
public enum HashAlgorithmDto
{
    /// <summary>MD5 hash algorithm (128-bit hash).</summary>
    Md5 = 0,
    /// <summary>SHA-1 hash algorithm (160-bit hash).</summary>
    Sha1 = 1,
    /// <summary>SHA-256 hash algorithm (256-bit hash).</summary>
    Sha256 = 2,
    /// <summary>SHA-384 hash algorithm (384-bit hash).</summary>
    Sha384 = 3,
    /// <summary>SHA-512 hash algorithm (512-bit hash).</summary>
    Sha512 = 4,
}

/// <summary>
/// Represents the input format for hash calculation.
/// </summary>
public enum HashInputFormatDto
{
    /// <summary>Input is treated as a UTF-8 encoded string.</summary>
    String = 0,
    /// <summary>Input is treated as a Base64 encoded string that will be decoded before hashing.</summary>
    Base64 = 1,
}

/// <summary>
/// Configuration for a hash calculation transformation node that computes cryptographic hash values.
/// </summary>
[NodeName("Hash", 1)]
public record HashNodeConfiguration : SourceTargetPathNodeConfiguration
{
    /// <summary>
    /// Gets or sets the relative path to the source value within the selected objects.
    /// </summary>
    [PropertyGroup("Paths", 2, "jsonpath")]
    public required string SourcePath { get; init; }

    /// <summary>
    /// Specifies the hash algorithm to use for calculating the hash value.
    /// </summary>
    [PropertyGroup("Options", 0)]
    public required HashAlgorithmDto Algorithm { get; init; }

    /// <summary>
    /// Specifies the input format for the data to be hashed.
    /// </summary>
    [PropertyGroup("Options", 1)]
    public HashInputFormatDto InputFormat { get; init; } = HashInputFormatDto.String;
}

/// <summary>
/// A transformation node that calculates cryptographic hash values from input data.
/// </summary>
[NodeConfiguration(typeof(HashNodeConfiguration))]
public class HashNode(NodeDelegate next) : IPipelineNode
{
    /// <inheritdoc />
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<HashNodeConfiguration>();

        if (dataContext.GetKind("$") == DataKind.Null || dataContext.GetKind("$") == DataKind.Undefined)
        {
            throw PipelineExecutionException.InputValueNull(nodeContext);
        }

        var sourcePath = JsonNodePath.NormalizePathOrRelative(c.SourcePath);
        var targetPath = JsonNodePath.NormalizePathOrRelative(c.TargetPath);

        var matchCount = 0;
        await dataContext.UpdateMatchesAsync(c.Path, matchCtx =>
        {
            matchCount++;
            if (matchCtx.GetKind("$") != DataKind.Object)
            {
                return Task.CompletedTask;
            }

            var sourceTokenValue = matchCtx.Get<JsonNode>(sourcePath);
            if (sourceTokenValue is null)
            {
                nodeContext.Error("Null value found at path '{0}.{1}' - hash calculation requires non-null input", c.Path, c.SourcePath);
                throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Null value found at path '{c.Path}.{c.SourcePath}' - hash calculation requires non-null input");
            }

            var sourceValue = JsonStringifyHelper.ToLegacyString(sourceTokenValue) ?? string.Empty;
            var hashValue = CalculateHash(sourceValue, c, nodeContext);
            matchCtx.Set(targetPath, JsonValue.Create(hashValue));
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        if (matchCount == 0)
        {
            nodeContext.Warning("No source data found at path '{0}'", c.Path);
            return;
        }

        await next(dataContext, nodeContext).ConfigureAwait(false);
    }

    private static string CalculateHash(string input, HashNodeConfiguration config, INodeContext nodeContext)
    {
        byte[] inputBytes;

        try
        {
            inputBytes = config.InputFormat switch
            {
                HashInputFormatDto.String => Encoding.UTF8.GetBytes(input),
                HashInputFormatDto.Base64 => Convert.FromBase64String(input),
                _ => throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Unsupported input format: {config.InputFormat}")
            };
        }
        catch (FormatException ex) when (config.InputFormat == HashInputFormatDto.Base64)
        {
            nodeContext.Error("Failed to decode Base64 input '{0}': {1}", input, ex.Message);
            throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Failed to decode Base64 input - {ex.Message}", ex);
        }

        byte[] hashBytes;

        using (HashAlgorithm hashAlgorithm = config.Algorithm switch
        {
            HashAlgorithmDto.Md5 => MD5.Create(),
            HashAlgorithmDto.Sha1 => SHA1.Create(),
            HashAlgorithmDto.Sha256 => SHA256.Create(),
            HashAlgorithmDto.Sha384 => SHA384.Create(),
            HashAlgorithmDto.Sha512 => SHA512.Create(),
            _ => throw new PipelineExecutionException($"[{nodeContext.NodePath}]: Unsupported hash algorithm: {config.Algorithm}")
        })
        {
            hashBytes = hashAlgorithm.ComputeHash(inputBytes);
        }

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
