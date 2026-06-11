namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;


internal interface IEtlContextAccessor<TContext> where TContext : class, IEtlContext
{
    Func<TContext>? EtlContextFactory { get; set; } 
    TContext GetEtlContext();
}

internal class EtlContextAccessor<TContext> : IEtlContextAccessor<TContext> where TContext : class, IEtlContext
{
    public Func<TContext>? EtlContextFactory { get; set; } 
    
    public TContext GetEtlContext()
    {
        if(EtlContextFactory == null)
        {
            throw DataPipelineException.EtlContextFactoryNotSet(typeof(TContext));
        }

        return EtlContextFactory();
    }
}