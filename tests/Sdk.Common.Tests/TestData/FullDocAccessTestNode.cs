using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.Tests.TestData;

/// <summary>
/// Result interface for tracking what FullDocAccessTestNode found.
/// </summary>
internal interface IFullDocAccessResult
{
    void RecordFound(bool found);
}

internal class FullDocAccessResult : IFullDocAccessResult
{
    public void RecordFound(bool found) { }
}

/// <summary>
/// Configuration for a test node that reads from a source path
/// (simulating what real pipeline nodes like FormatStringNode, MathNode etc. do).
/// </summary>
[NodeName("FullDocAccessTest", 1)]
internal record FullDocAccessTestNodeConfiguration : TargetPathNodeConfiguration
{
    public string SourcePath { get; set; } = "$.full";
}

/// <summary>
/// Test node that reads data via dataContext.Get&lt;JsonNode&gt;(path) — the new
/// path-only API equivalent of the legacy Current.SelectToken pattern. Used to
/// verify that values written from sibling pipelines are visible.
/// </summary>
[NodeConfiguration(typeof(FullDocAccessTestNodeConfiguration))]
internal class FullDocAccessTestNode(NodeDelegate next, IFullDocAccessResult result) : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<FullDocAccessTestNodeConfiguration>();

        var node = dataContext.Get<JsonNode>(c.SourcePath);
        result.RecordFound(node != null);

        // Write the found value (or null) to the target path
        dataContext.Set(c.TargetPath, node, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }
}
