using System.Text.Json.Nodes;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538 / handover §11 (Q12): the debugger reports the JSONPath of every value it masked as
/// <c>***</c>, rooted at the snapshot object (<c>$.input…</c>, <c>$.output…</c>).
/// </summary>
public class DebugSnapshotRedactedPathsTests
{
    // Obviously fake test values, never real credentials.
    private const string Secret = "fake-revealed-password-42";
    private const string OtherSecret = "fake-client-secret-0007";

    private static (DefaultPipelineDebugger Debugger, PipelineSecretRegistry Registry) NewDebugger()
    {
        var debugger = new DefaultPipelineDebugger(NullLoggerFactory.Instance);
        debugger.RegisterPipelineRtEntityId(new RtEntityId("Test/Pipeline", OctoObjectId.GenerateNewId()),
            Guid.NewGuid());
        var registry = new PipelineSecretRegistry();
        debugger.AddSecretRegistry(registry);
        return (debugger, registry);
    }

    [Fact]
    public void Output_MaskedValues_AreReportedWithTheirPaths()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogOutput("0:n", new NodePath("n"), null, 0, JsonNode.Parse($$$"""
            {"smtp":{"user":"svc","password":"{{{Secret}}}"},"header":"Bearer {{{Secret}}}","port":25}
            """));

        var paths = debugger.GetRedactedPaths("0:n");
        Assert.Equal(["$.output.header", "$.output.smtp.password"], paths);
        var output = JsonNode.Parse(debugger.GetDebugInformation().DebugPoints.Single().Output!)!;
        Assert.Equal("***", output["smtp"]!["password"]!.GetValue<string>());
        Assert.Equal("Bearer ***", output["header"]!.GetValue<string>());
    }

    [Fact]
    public void InputAndOutput_AreReportedUnderTheirOwnRoots()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogInput("0:n", new NodePath("n"), null, 0,
            JsonNode.Parse($$$"""{"config":{"clientSecret":"{{{Secret}}}"}}"""));
        debugger.LogOutput("0:n", new NodePath("n"), null, 0,
            JsonNode.Parse($$$"""{"items":[{"token":"x"},{"token":"{{{Secret}}}"}]}"""));

        Assert.Equal(["$.input.config.clientSecret", "$.output.items[1].token"],
            debugger.GetRedactedPaths("0:n"));
        var info = debugger.GetDebugInformation();
        Assert.Equal(["$.input.config.clientSecret", "$.output.items[1].token"], info.RedactedPaths["0:n"]);
    }

    [Fact]
    public void GetDebugInformation_CarriesRedactedPathsOnTheTransportDto()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogInput("0:a", new NodePath("a"), null, 0,
            JsonNode.Parse($$$"""{"config":{"clientSecret":"{{{Secret}}}"}}"""));
        debugger.LogOutput("0:a", new NodePath("a"), null, 0,
            JsonNode.Parse($$$"""{"smtp":{"password":"{{{Secret}}}"}}"""));
        debugger.LogOutput("1:b", new NodePath("b"), null, 1, JsonNode.Parse("""{"port":25}"""));

        var debugPoints = debugger.GetDebugInformation().DebugPoints.ToDictionary(p => p.NodeId);

        Assert.Equal(["$.input.config.clientSecret", "$.output.smtp.password"],
            debugPoints["0:a"].RedactedPaths);
        Assert.Null(debugPoints["1:b"].RedactedPaths);
    }

    [Fact]
    public void ArrayItemsAndNonIdentifierKeys_UseBracketNotation()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogOutput("0:n", new NodePath("n"), null, 0, JsonNode.Parse($$$"""
            {"values":["a","{{{Secret}}}"],"http headers":{"x-api-key":"{{{Secret}}}"},"it's":"{{{Secret}}}"}
            """));

        Assert.Equal(["$.output.values[1]", "$.output['http headers']['x-api-key']", "$.output['it\\'s']"],
            debugger.GetRedactedPaths("0:n"));
    }

    [Fact]
    public void BareStringRoot_IsReportedWithTheRootPath()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogOutput("0:n", new NodePath("n"), null, 0, JsonValue.Create(Secret));

        Assert.Equal(["$.output"], debugger.GetRedactedPaths("0:n"));
    }

    [Fact]
    public void ValuesOfSeveralRegistries_AreReportedOnce()
    {
        var (debugger, registry) = NewDebugger();
        var second = new PipelineSecretRegistry();
        debugger.AddSecretRegistry(second);
        registry.Register(Secret);
        second.Register(OtherSecret);
        second.Register(Secret);

        debugger.LogOutput("0:n", new NodePath("n"), null, 0,
            JsonNode.Parse($$$"""{"a":"{{{Secret}}}","b":"{{{OtherSecret}}}"}"""));

        Assert.Equal(["$.output.a", "$.output.b"], debugger.GetRedactedPaths("0:n"));
    }

    [Fact]
    public void NothingMasked_ReportsNoPaths()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogOutput("0:n", new NodePath("n"), null, 0, JsonNode.Parse("""{"a":"public"}"""));

        Assert.Null(debugger.GetRedactedPaths("0:n"));
        Assert.Empty(debugger.GetDebugInformation().RedactedPaths);
    }

    [Fact]
    public void Recapture_WithoutSecret_ReplacesThePathsOfThatSnapshot()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.LogOutput("0:n", new NodePath("n"), null, 0, JsonNode.Parse($$$"""{"a":"{{{Secret}}}"}"""));
        debugger.LogOutput("0:n", new NodePath("n"), null, 0, JsonNode.Parse("""{"a":"public"}"""));

        Assert.Null(debugger.GetRedactedPaths("0:n"));
    }

    [Fact]
    public void DryRunIntent_IsReportedUnderItsRoot()
    {
        var (debugger, registry) = NewDebugger();
        registry.Register(Secret);

        debugger.RecordDryRunIntent("0:n", new NodePath("n"), null, 0, "ApplyChanges@2",
            JsonNode.Parse($$$"""{"refreshToken":"{{{Secret}}}"}"""));

        Assert.Equal(["$.dryRunIntent.refreshToken"], debugger.GetRedactedPaths("0:n"));
    }

    [Fact]
    public void Registry_RedactWithoutPathCollection_StillMasks()
    {
        var registry = new PipelineSecretRegistry();
        registry.Register(Secret);

        var redacted = registry.Redact(JsonNode.Parse($$$"""{"a":["{{{Secret}}}"]}"""));

        Assert.Equal("""{"a":["***"]}""", redacted!.ToJsonString());
    }
}
