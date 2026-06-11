using Meshmakers.Octo.Sdk.Common.Adapters;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline;
using Meshmakers.Octo.Sdk.SimulationNodes;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Plug.Simulation;

var plugBuilder = new AdapterBuilder();

plugBuilder.Run(args, (_, services) =>
{
    services.AddDataPipeline()
        .AddSimulationNodes()
        .RegisterEtlContext<IEtlContext>();
    services.AddSingleton<IAdapterService, SimulationAdapterService>();
});