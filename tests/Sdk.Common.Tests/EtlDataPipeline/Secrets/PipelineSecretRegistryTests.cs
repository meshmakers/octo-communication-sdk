using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538: value-based masking of revealed secrets and resolved credentials.
/// </summary>
public class PipelineSecretRegistryTests
{
    // Obviously fake test value, never a real credential.
    private const string Secret = "fake-secret-value-0001";

    [Fact]
    public void Redact_WithoutRegistration_ReturnsInputUnchanged()
    {
        var registry = new PipelineSecretRegistry();
        const string text = "nothing to hide";

        Assert.False(registry.HasSecrets);
        Assert.Same(text, registry.Redact(text));
    }

    [Fact]
    public void Redact_ExactMatch_ReturnsMask()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);

        Assert.True(registry.HasSecrets);
        Assert.Equal(PipelineSecretRegistry.Mask, registry.Redact(Secret));
    }

    [Fact]
    public void Redact_Substring_ReplacesEveryOccurrence()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);

        var redacted = registry.Redact($"Bearer {Secret} and again {Secret}.");

        Assert.Equal("Bearer *** and again ***.", redacted);
    }

    [Fact]
    public void Redact_ShortValue_IsMaskedOnlyAsWholeString()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register("abc");

        Assert.Equal(PipelineSecretRegistry.Mask, registry.Redact("abc"));
        Assert.Equal("xabcx", registry.Redact("xabcx"));
    }

    [Fact]
    public void Redact_OverlappingValues_ReplacesTheLongerOneAsAWhole()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register("fake-token");
        registry.Register("fake-token-extended");

        Assert.Equal("x *** y", registry.Redact("x fake-token-extended y"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_BlankValue_IsIgnored(string? value)
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(value);

        Assert.False(registry.HasSecrets);
    }

    [Fact]
    public void RedactNode_WithoutMatch_ReturnsSameInstance()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);
        var node = JsonNode.Parse("""{"a":"public","b":[1,"x"]}""")!;

        Assert.Same(node, registry.Redact(node));
    }

    [Fact]
    public void RedactNode_MasksNestedStringsAndLeavesTheInputUntouched()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);
        var node = JsonNode.Parse(
            $$"""{"token":"{{Secret}}","header":"Bearer {{Secret}}","list":[{"v":"{{Secret}}"},"ok",3],"n":1}""")!;
        var original = node.ToJsonString();

        var redacted = registry.Redact(node)!;

        Assert.Equal(original, node.ToJsonString());
        Assert.DoesNotContain(Secret, redacted.ToJsonString());
        Assert.Equal("***", redacted["token"]!.GetValue<string>());
        Assert.Equal("Bearer ***", redacted["header"]!.GetValue<string>());
        Assert.Equal("***", redacted["list"]![0]!["v"]!.GetValue<string>());
        Assert.Equal("ok", redacted["list"]![1]!.GetValue<string>());
        Assert.Equal(1, redacted["n"]!.GetValue<int>());
    }

    [Fact]
    public void RedactNode_StringRoot_IsMasked()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);

        var redacted = registry.Redact(JsonValue.Create(Secret));

        Assert.Equal("***", redacted!.GetValue<string>());
    }

    [Fact]
    public void RedactArgument_MasksStringsAndNodes_PassesOthersThrough()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);

        Assert.Equal("***", registry.RedactArgument(Secret));
        Assert.Equal(42, registry.RedactArgument(42));
        var node = (JsonNode)registry.RedactArgument(JsonNode.Parse($$"""{"x":"{{Secret}}"}"""))!;
        Assert.Equal("***", node["x"]!.GetValue<string>());
    }
}
