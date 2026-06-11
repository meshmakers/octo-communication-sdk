using LiteDB;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Buffering.EdgeBuffer;

internal static class LiteDatabaseExtensions
{
    public static OperationResult<T> WithTransaction<T>(this ILiteDatabase database, ILogger logger, Func<T> a)
    {
        try
        {
            database.BeginTrans();
            var result = a();
            database.Commit();
            return OperationResult<T>.Ok(result);
        }
        catch (Exception e)
        {
            database.Rollback();
            logger.LogError(e, "Error with database action");
            return OperationResult<T>.Error();
        }
    }
}