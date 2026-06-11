using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Implements an extract-transform-load data orchestrator
/// </summary>
public class EtlDataOrchestrator : IEtlDataOrchestrator
{
    private readonly IServiceProvider _globalServiceProvider;
    private readonly INodeLookupService _nodeLookupService;

    /// <summary>
    /// Creates a new instance of <see cref="EtlDataOrchestrator"/>
    /// </summary>
    /// <param name="globalServiceProvider"></param>
    /// <param name="nodeLookupService"></param>
    public EtlDataOrchestrator(IServiceProvider globalServiceProvider, INodeLookupService nodeLookupService)
    {
        _globalServiceProvider = globalServiceProvider;
        _nodeLookupService = nodeLookupService;
    }

    /// <inheritdoc />
    public async Task<object?> ExecutePipelineAsync<TEtlContext>(NodeDefinitionRoot nodeDefinitionRoot,
        TEtlContext etlContext, IPipelineDebugger? pipelineDebugger = null, object? value = null)
        where TEtlContext : class, IEtlContext
    {
        var logger = pipelineDebugger?.Logger ?? _globalServiceProvider.GetRequiredService<IPipelineLogger>();

        // Create a new scope per execution;
        // we can't dispose the scope here, because some nodes will create their sub pipeline, which will outlive this scope.
        var scope = _globalServiceProvider.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var contextAccessor = serviceProvider.GetRequiredService<IEtlContextAccessor<TEtlContext>>();
        contextAccessor.EtlContextFactory = () => etlContext;

        // Owns the JsonDocument when CreateDataContextFromValue parsed one; the `using`
        // releases the document's pooled buffers at scope exit. Get<JsonNode>("$") below
        // returns a tree independent of the document (either a fresh Parse from raw text
        // or the overlay's _lifted JsonNode), so disposal after the return value is
        // computed is safe.
        using var dataContext = CreateDataContextFromValue(value);
        var rootNodeContext = NodeContext.CreateRootNodeContext(serviceProvider, logger, dataContext, pipelineDebugger);

        pipelineDebugger?.BeginPipelineExecution();

        if (nodeDefinitionRoot.Transformations == null)
        {
            rootNodeContext.Warning("No transformations found in the pipeline configuration");
            return null;
        }

        // This is the last delegate in the sequence -> it will call the next node in the pipeline
        NodeDelegate nextDelegate = (ds, nc) =>
        {
            nc.Unregister(ds);

            // Mirror only when we ran in a sub-context. When ds is the outer
            // dataContext (no sub-context layer), Set("$", Get<JsonNode>("$"))
            // would lift the entire base document into the overlay and defeat
            // every HasWrites fast path. See migration spec §5.1.
            if (!ReferenceEquals(ds, dataContext))
            {
                dataContext.Set("$", ds.Get<JsonNode>("$"));
            }
            return Task.CompletedTask;
        };

        try
        {
            rootNodeContext.Info("Executing pipeline");

            uint sequenceNumber = 0;
            foreach (var nodeConfiguration in nodeDefinitionRoot.Transformations.Reverse())
            {
                if (!_nodeLookupService.TryGetNodeConfigurationQualifiedName(nodeConfiguration.GetType(),
                        out var nodeQualifiedName))
                {
                    throw DataPipelineException.UnknownConfigurationType(nodeConfiguration.GetType());
                }

                if (!_nodeLookupService.TryCreateInstance(serviceProvider, nodeQualifiedName!, nextDelegate,
                        out var node))
                {
                    throw DataPipelineException.UnknownObjectPipelineNode(nodeQualifiedName!);
                }


                // This is the next delegate in the sequence -> it will call the next node in the sequence
                nextDelegate = async (ds, nc) =>
                {
                    if (nc != rootNodeContext)
                    {
                        nc.Unregister(ds);
                    }

                    var nodeContext = rootNodeContext.RegisterChildNode(nodeQualifiedName!, sequenceNumber++,
                        nodeConfiguration, ds);
                    nodeContext.Debug("Forward Executing");
                    try
                    {
                        await node!.ProcessObjectAsync(ds, nodeContext);
                    }
                    catch (DataPipelineException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        nodeContext.Error(e, "Error executing node");
                        throw DataPipelineException.NodeExecutionFailed(nodeContext.NodePath, e);
                    }

                    nodeContext.Debug("Reverse completed");
                };
            }

            await nextDelegate(dataContext, rootNodeContext);
            rootNodeContext.Unregister(dataContext);
            rootNodeContext.Info("Pipeline completed");
        }
        catch (Exception e)
        {
            rootNodeContext.Error(e, "Error during pipeline execution");
            throw;
        }
        finally
        {
            try
            {
                // end debugging
                if (pipelineDebugger != null)
                {
                    await pipelineDebugger.EndPipelineExecutionAsync();
                }
            }
            catch (Exception e)
            {
                rootNodeContext.Error(e, "Error during sending debug information");
            }
        }

        return dataContext.Get<JsonNode>("$");
    }

    private static DataContextImpl CreateDataContextFromValue(object? value)
    {
        if (value is null)
        {
            return new DataContextImpl();
        }

        if (value is JsonNode node)
        {
            var json = node.ToJsonString(SystemTextJsonOptions.Default);
            return new DataContextImpl(JsonDocument.Parse(json));
        }

        if (value is JsonDocument doc)
        {
            return new DataContextImpl(doc);
        }

        if (value is JsonElement element)
        {
            return new DataContextImpl(element);
        }

        // Fall back to serializing arbitrary CLR objects.
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value, SystemTextJsonOptions.Default);
        return new DataContextImpl(JsonDocument.Parse(serialized));
    }
}
