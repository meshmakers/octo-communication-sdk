using NLog;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Runs an adapter host and turns its outcome into the process exit code: 0 for a normal stop or a
///     shutdown requested while starting, <see cref="FailureExitCode" /> for any other exception.
/// </summary>
internal static class AdapterHostRunGuard
{
    internal const int FailureExitCode = 1;

    private const int SuccessExitCode = 0;

    internal static int Run(Action runHost, Func<bool> isStopRequested, Logger logger)
    {
        try
        {
            runHost();
            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            return ExitCodeFor(exception, isStopRequested, logger);
        }
    }

    internal static async Task<int> RunAsync(Func<Task> runHost, Func<bool> isStopRequested, Logger logger)
    {
        try
        {
            await runHost();
            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            return ExitCodeFor(exception, isStopRequested, logger);
        }
    }

    private static int ExitCodeFor(Exception exception, Func<bool> isStopRequested, Logger logger)
    {
        var isRequestedShutdown = exception is OperationCanceledException && isStopRequested();
        if (isRequestedShutdown)
        {
            logger.Info("Adapter stopped because a shutdown was requested while it was starting");
            return SuccessExitCode;
        }

        logger.Error(exception, "Adapter stopped because of an exception: {ExceptionType}: {Reason}",
            exception.GetType().FullName, exception.Message);
        return FailureExitCode;
    }
}
