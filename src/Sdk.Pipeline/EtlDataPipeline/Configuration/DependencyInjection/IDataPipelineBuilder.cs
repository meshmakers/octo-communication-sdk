using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration.DependencyInjection;

/// <summary>
/// Builder for data pipeline
/// </summary>
public interface IDataPipelineBuilder
{
    /// <summary>
    ///     Gets the services.
    /// </summary>
    /// <value>
    ///     The services.
    /// </value>
    IServiceCollection Services { get; }

    /// <summary>
    /// Register a node configuration
    /// </summary>
    /// <param name="nodeConfigurationType">Type of node configuration to register</param>
    /// <returns></returns>
    IDataPipelineBuilder RegisterNodeConfiguration(Type nodeConfigurationType);

    /// <summary>
    /// Register a node configuration
    /// </summary>
    /// <typeparam name="TNodeType">Type of node configuration to register</typeparam>
    /// <returns></returns>
    IDataPipelineBuilder RegisterNodeConfiguration<TNodeType>() where TNodeType : INodeConfiguration;

    /// <summary>
    /// Register a node
    /// </summary>
    /// <param name="nodeType">Type of node to register</param>
    IDataPipelineBuilder RegisterNode(Type nodeType);
    
    /// <summary>
    /// Register a node
    /// </summary>
    /// <typeparam name="TNodeType">Type of node to register</typeparam>
    /// <returns></returns>
    IDataPipelineBuilder RegisterNode<TNodeType>() where TNodeType : IPipelineNode;


    /// <summary>
    /// Register a trigger node
    /// </summary>
    /// <param name="nodeType">Type of node to register</param>
    /// <returns></returns>
    IDataPipelineBuilder RegisterTriggerNode(Type nodeType);
    
    /// <summary>
    /// Register a trigger node
    /// </summary>
    /// <typeparam name="TNodeType">Type of node to register</typeparam>
    /// <returns></returns>
    IDataPipelineBuilder RegisterTriggerNode<TNodeType>() where TNodeType : ITriggerPipelineNode;

    /// <summary>
    /// Register the context for a Retriever.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <returns></returns>
    public IDataPipelineBuilder RegisterEtlContext<TContext>() where TContext : class, IEtlContext;
}