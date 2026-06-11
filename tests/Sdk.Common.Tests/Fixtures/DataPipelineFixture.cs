using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Common.Tests.TestData;

namespace Sdk.Common.Tests.Fixtures;

public class DataPipelineFixture : ServiceCollectionFixture
{
    public DataPipelineFixture()
    {
        DataPipelineBuilder = Services.AddDataPipeline()
            .RegisterNode<TestDataExtractNode>()
            .RegisterNode<TestNode>()
            .RegisterNode<ExceptionNode>()
            .RegisterNode<TestOutputNode>();
    }

    public IDataPipelineBuilder DataPipelineBuilder { get; }
}