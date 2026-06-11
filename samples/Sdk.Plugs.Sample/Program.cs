using Meshmakers.Octo.Sdk.Common.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Sdk.Plugs.Sample;

var plugBuilder = new AdapterBuilder();

plugBuilder.Run(args, (_, services) =>
{
    services.AddSingleton<IAdapterService, DemoAdapterService>();
    services.AddDataPipeline();
});