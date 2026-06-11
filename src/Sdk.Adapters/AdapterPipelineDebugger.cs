using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
/// Implements a default pipeline debugger
/// </summary>
public sealed class AdapterPipelineDebugger : DefaultPipelineDebugger
{
    private readonly IAdapterHubClient _adapterHubClient;

    /// <summary>
    /// Creates a new instance of <see cref="T:Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger.DefaultPipelineDebugger" />
    /// </summary>
    /// <param name="adapterHubClient"></param>
    /// <param name="loggerFactory"></param>
    public AdapterPipelineDebugger(IAdapterHubClient adapterHubClient, ILoggerFactory loggerFactory)
    : base(loggerFactory)
    {
        _adapterHubClient = adapterHubClient;
    }

    /// <inheritdoc />
    public override async Task EndPipelineExecutionAsync()
    {
        var debugInformationRoot = GetDebugInformation();
        if (PipelineRtEntityId == null)
        {
            throw PipelineDebuggerException.PipelineRtEntityIdNotSet();
        }
        if (PipelineExecutionId == null)
        {
            throw PipelineDebuggerException.PipelineExecutionIdNotSet();
        }
        
        foreach (var debugPointDto in debugInformationRoot.DebugPoints)
        {
            await _adapterHubClient.SendDebugDataAsync(PipelineRtEntityId.Value, PipelineExecutionId.Value, debugPointDto);
        }
    }
}