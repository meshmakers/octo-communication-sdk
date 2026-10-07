using System.Text.Json.Nodes;
using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sdk.Common.Tests.EtlDataPipeline.Secrets;

/// <summary>
/// AB#5538 review: the controller ships configuration entities with Secret values revealed. A node that
/// copies a configuration into the data context registers those values first, so the debug panel,
/// logs and persisted results show <c>***</c>.
/// </summary>
public class ConfigurationSecretsTests
{
    // Obviously fake test values, never real credentials.
    private const string Password = "fake-smtp-password-1";
    private const string RecordKey = "fake-record-api-key-2";
    private const string ConfigName = "smtp";
    private static readonly RtCkId<CkTypeId> ConfigType = "System.Communication/EMailSenderConfiguration";

    private const string ConfigJson = $$"""
        {
          "rtId": "65d5c447b420da3fb12381bc",
          "attributes": {
            "Host": "smtp.example.com",
            "Username": "user",
            "Password": "{{Password}}",
            "Overrides": [ { "Key": "a", "Value": "{{RecordKey}}" } ]
          }
        }
        """;

    private static (DataContextImpl, INodeContext, DefaultPipelineDebugger, IPipelineLogger) Context()
    {
        var dataContext = new DataContextImpl();
        var debugger = new DefaultPipelineDebugger(NullLoggerFactory.Instance);
        debugger.RegisterPipelineRtEntityId(new RtEntityId("Test/Pipeline", OctoObjectId.GenerateNewId()),
            Guid.NewGuid());
        var logger = A.Fake<IPipelineLogger>();
        var root = NodeContext.CreateRootNodeContext(new ServiceCollection().BuildServiceProvider(), logger,
            dataContext, debugger);
        var config = new GetPipelineConfigByWellKnownNameNodeConfiguration
        {
            WellKnownName = ConfigName, TargetPath = "$.config"
        };
        return (dataContext, root.RegisterChildNode("GetPipelineConfigByWellKnownName@1", 0, config, dataContext),
            debugger, logger);
    }

    private static IEtlContext EtlContext()
    {
        var etl = A.Fake<IEtlContext>();
        A.CallTo(() => etl.TenantId).Returns("tenant");
        A.CallTo(() => etl.GlobalConfiguration).Returns(new GlobalConfiguration(
        [
            new ConfigurationDto(OctoObjectId.GenerateNewId(), ConfigType, ConfigName, ConfigJson)
        ]));
        return etl;
    }

    private static IConfigurationSecretAttributeResolver Resolver(IReadOnlyCollection<string>? names)
    {
        var resolver = A.Fake<IConfigurationSecretAttributeResolver>();
        A.CallTo(() => resolver.GetSecretAttributeNames("tenant", A<RtCkId<CkTypeId>>._)).Returns(names);
        return resolver;
    }

    private static async Task<string> RunAndCaptureSnapshot(IConfigurationSecretAttributeResolver? resolver,
        Action<DataContextImpl, IPipelineLogger>? assert = null)
    {
        var (dc, nc, debugger, logger) = Context();
        await new GetPipelineConfigByWellKnownNameNode(A.Fake<NodeDelegate>(), EtlContext(), resolver)
            .ProcessObjectAsync(dc, nc);
        assert?.Invoke(dc, logger);
        nc.Unregister(dc);
        return debugger.GetDebugInformation().DebugPoints.Single(p => p.Output != null).Output!;
    }

    [Fact]
    public async Task ResolvedSecretAttributes_AreMaskedInTheSnapshot_ButStayInTheData()
    {
        var output = await RunAndCaptureSnapshot(Resolver(["Password", "Value"]), (dc, _) =>
        {
            // The node that needs the credential still reads it.
            Assert.Equal(Password, dc.Get<string>("$.config.attributes.Password"));
        });

        Assert.DoesNotContain(Password, output);
        Assert.DoesNotContain(RecordKey, output);
        Assert.Contains("smtp.example.com", output);
        Assert.Equal("***", JsonNode.Parse(output)!["config"]!["attributes"]!["Password"]!.GetValue<string>());
    }

    [Fact]
    public async Task ResolvedTypeWithoutSecrets_MasksNothing()
    {
        var output = await RunAndCaptureSnapshot(Resolver([]));

        Assert.Contains(Password, output);
    }

    [Fact]
    public async Task UnresolvedType_FallsBackToTheKnownCredentialNames_AndWarnsWithoutValues()
    {
        var output = await RunAndCaptureSnapshot(Resolver(null), (_, logger) =>
        {
            var warnings = Fake.GetCalls(logger).Where(c => c.Method.Name == nameof(IPipelineLogger.Warning)).ToList();
            Assert.Single(warnings);
            var rendered = string.Join("|", warnings.SelectMany(c => c.Arguments)
                .SelectMany(a => a is object[] arr ? arr : new[] { a }).Select(a => $"{a}"));
            Assert.DoesNotContain(Password, rendered);
        });

        Assert.DoesNotContain(Password, output);
        // "Value" is no known credential name; only a resolved type masks it.
        Assert.Contains(RecordKey, output);
    }

    [Fact]
    public async Task WithoutAResolver_FallsBackToTheKnownCredentialNames()
    {
        var output = await RunAndCaptureSnapshot(null);

        Assert.DoesNotContain(Password, output);
    }

    [Fact]
    public async Task AThrowingResolver_FallsBackToTheKnownCredentialNames()
    {
        var resolver = A.Fake<IConfigurationSecretAttributeResolver>();
        A.CallTo(() => resolver.GetSecretAttributeNames(A<string>._, A<RtCkId<CkTypeId>>._))
            .Throws(new InvalidOperationException("boom"));

        var output = await RunAndCaptureSnapshot(resolver);

        Assert.DoesNotContain(Password, output);
    }

    [Fact]
    public void Register_MatchesTypedCamelCasePropertiesAndNestedRecords()
    {
        var (_, nc, _, _) = Context();
        var json = JsonNode.Parse($$"""{"password":"{{Password}}","settings":{"apiKey":"{{RecordKey}}"},"host":"h"}""");

        var count = ConfigurationSecrets.Register(nc, Resolver(["Password", "ApiKey"]), "tenant", ConfigType, json,
            ConfigName);

        Assert.Equal(2, count);
        Assert.Equal("***", nc.SecretRegistry!.Redact(Password));
        Assert.Equal("***", nc.SecretRegistry.Redact(RecordKey));
        Assert.Equal("h", nc.SecretRegistry.Redact("h"));
    }

    [Fact]
    public void GlobalConfiguration_ReturnsTheConfigurationTypeId()
    {
        var global = EtlContext().GlobalConfiguration;

        Assert.Equal(ConfigType, global.GetConfigurationTypeId("SMTP"));
        Assert.Null(global.GetConfigurationTypeId("unknown"));
    }
}
