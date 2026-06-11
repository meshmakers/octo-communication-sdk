namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Interface for extract pipeline nodes
/// </summary>
public interface ITriggerPipelineNode
{
    /// <summary>
    /// Starts the triggerable extract node
    /// </summary>
    /// <param name="context">Context information about the environment in which the node is executed</param>
    /// <returns></returns>
    Task StartAsync(ITriggerContext context);
    
    /// <summary>
    ///  Stops the triggerable extract node
    /// </summary>
    /// <param name="context">Context information about the environment in which the node is executed</param>
    /// <returns></returns>
    Task StopAsync(ITriggerContext context);
}