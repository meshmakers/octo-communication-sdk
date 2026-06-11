using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.Serializer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Control;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Extracts;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Loads;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Transforms.Aggregations;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Meshmakers.Octo.Sdk.Common.Services;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for adding Ck model compiler services to the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds Octo Mesh data pipeline serializer services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services"></param>
    // ReSharper disable once MemberCanBePrivate.Global
    public static IDataPipelineBuilder AddDataPipelineSerializer(this IServiceCollection services)
    {
        // Adding serializers
        services.AddSingleton<IPipelineConfigurationSerializer, YamlPipelineConfigurationSerializer>();
        services.AddSingleton<IJsonPipelineConfigurationSerializer, JsonPipelineConfigurationSerializer>();
        
        var pipelineBuilder = new DataPipelineBuilder(services);
        
        // Register control nodes
        pipelineBuilder.RegisterNodeConfiguration<SelectByPathNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<ForEachNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<ForNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<IfNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<SwitchNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<GroupNodeConfiguration>();

        // Register extract nodes
        pipelineBuilder.RegisterNodeConfiguration<GetPipelineConfigByWellKnownNameNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<SetArrayOfPrimitiveValuesNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<SetJsonNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<SetPrimitiveValueNodeConfiguration>();
        
        // Register load nodes
        pipelineBuilder.RegisterNodeConfiguration<ToPipelineDataEventNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<SetPipelineExecutionResultNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<ToWebhookNodeConfiguration>();

        // Register transform nodes
        pipelineBuilder.RegisterNodeConfiguration<ConcatNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<ConvertDataTypeNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<DistinctNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<FlattenNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<LinearScalerNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<LoggerNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<MapNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<PrintDebugNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<ProjectNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<FormatStringNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<ExecuteCSharpNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<MathNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<DateTimeNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<JoinNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<SumAggregationNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<Base64EncodeNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<Base64DecodeNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<TransformStringNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<HashNodeConfiguration>();

        // Register trigger nodes
#if NET10_0_OR_GREATER
        pipelineBuilder.RegisterNodeConfiguration<FromExecutePipelineCommandNodeConfiguration>();
#endif
        pipelineBuilder.RegisterNodeConfiguration<FromPipelineDataEventNodeConfiguration>();
        pipelineBuilder.RegisterNodeConfiguration<FromPollingNodeConfiguration>();
        
        return pipelineBuilder;
    }
    
    /// <summary>
    /// Adds ETL pipeline services
    /// </summary>
    /// <returns></returns>
    public static IDataPipelineBuilder AddDataPipeline(this IServiceCollection services)
    {
        var builder = services.AddDataPipelineSerializer();

        // Add orchestrator
        services.AddTransient<IPipelineLogger, DefaultPipelineLogger>();
        services.AddTransient<IPipelineDebugSerializer, PipelineDebugSerializer>();
        services.AddTransient<ICompressionService, CompressionService>();
        services.AddTransient<IEtlDataOrchestrator, EtlDataOrchestrator>();

        // Add services for nodes
        services.AddTransient<IPollingService, PollingService>();

        // Add buffer services (Phase 7 — restored after JsonNode-based LiteDB BSON converter)
        services.AddSingleton(typeof(IEdgeDataBuffer<>), typeof(EdgeDataBuffer<>));
        services.AddSingleton<ILiteDBFactory, LiteDbFileFactory>();
        services.AddHostedService<BufferSchedulerHostedService>();
        services.AddSingleton<IBufferScheduler, BufferScheduler>();

        services.AddSingleton<IContextCreatorService, DefaultContextCreatorService>();

        // EtlContext
        services.AddScoped(typeof(IEtlContextAccessor<>), typeof(EtlContextAccessor<>));

        // Register control nodes
        builder.RegisterNode<SelectByPathNode>();
        builder.RegisterNode<ForEachNode>();
        builder.RegisterNode<ForNode>();
        builder.RegisterNode<IfNode>();
        builder.RegisterNode<SwitchNode>();
        builder.RegisterNode<GroupNode>();

        // Register extract nodes
        builder.RegisterNode<SetArrayOfPrimitiveValuesNode>();
        builder.RegisterNode<SetJsonNode>();
        builder.RegisterNode<SetPrimitiveValueNode>();
        builder.RegisterNode<BufferRetrievalNode>();

        // Register extract nodes
        builder.RegisterNode<GetPipelineConfigByWellKnownNameNode>();

        // Register load nodes
        builder.RegisterNode<BufferNode>();
        builder.RegisterNode<ToPipelineDataEventNode>();
        builder.RegisterNode<SetPipelineExecutionResultNode>();
        builder.RegisterNode<ToWebhookNode>();
        services.AddHttpClient("Webhook");

        // Register transform nodes
        builder.RegisterNode<ConcatNode>();
        builder.RegisterNode<ConvertDataTypeNode>();
        builder.RegisterNode<DistinctNode>();
        builder.RegisterNode<FlattenNode>();
        builder.RegisterNode<LinearScalerNode>();
        builder.RegisterNode<LoggerNode>();
        builder.RegisterNode<MapNode>();
        builder.RegisterNode<PrintDebugNode>();
        builder.RegisterNode<ProjectNode>();
        builder.RegisterNode<FormatStringNode>();
        builder.RegisterNode<ExecuteCSharpNode>();
        builder.RegisterNode<MathNode>();
        builder.RegisterNode<DateTimeNode>();
        builder.RegisterNode<JoinNode>();
        builder.RegisterNode<SumAggregationNode>();
        builder.RegisterNode<Base64EncodeNode>();
        builder.RegisterNode<Base64DecodeNode>();
        builder.RegisterNode<TransformStringNode>();
        builder.RegisterNode<HashNode>();

        // Register trigger nodes
#if NET10_0_OR_GREATER
        builder.RegisterTriggerNode<FromExecutePipelineCommandNode>();
#endif
        builder.RegisterTriggerNode<FromPipelineDataEventNode>();
        builder.RegisterTriggerNode<FromPollingNode>();
        
        return builder;
    }
}