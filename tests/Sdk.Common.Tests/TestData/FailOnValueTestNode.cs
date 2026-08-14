using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;

namespace Sdk.Common.Tests.TestData;

/// <summary>
/// Configuration for a test node that fails for one specific item value and succeeds
/// for every other — the "poisoned element" in loop error-isolation tests.
/// </summary>
[NodeName("FailOnValueTest", 1)]
internal record FailOnValueTestNodeConfiguration : TargetPathNodeConfiguration
{
    /// <summary>
    /// Path to the value the node reads to decide whether to fail.
    /// </summary>
    public string SourcePath { get; set; } = "$.key.Id";

    /// <summary>
    /// The value that makes the node throw.
    /// </summary>
    public required int FailOnValue { get; set; }

    /// <summary>
    /// Throw <see cref="OperationCanceledException"/> instead of <see cref="MyCustomException"/>.
    /// </summary>
    public bool ThrowCanceled { get; set; }
}

/// <summary>
/// Test node that throws for the configured item value and otherwise counts the
/// execution and writes the read value to the target path.
/// </summary>
[NodeConfiguration(typeof(FailOnValueTestNodeConfiguration))]
internal class FailOnValueTestNode(NodeDelegate next, ITestCounter testCounter) : IPipelineNode
{
    public async Task ProcessObjectAsync(IDataContext dataContext, INodeContext nodeContext)
    {
        var c = nodeContext.GetNodeConfiguration<FailOnValueTestNodeConfiguration>();

        var value = dataContext.Get<int>(c.SourcePath);
        if (value == c.FailOnValue)
        {
            if (c.ThrowCanceled)
            {
                throw new OperationCanceledException($"Canceled on item {value}");
            }

            throw new MyCustomException($"Poisoned item {value}");
        }

        testCounter.GetNext();
        dataContext.Set(c.TargetPath, value, c.DocumentMode, c.TargetValueKind, c.TargetValueWriteMode);

        await next(dataContext, nodeContext);
    }
}
