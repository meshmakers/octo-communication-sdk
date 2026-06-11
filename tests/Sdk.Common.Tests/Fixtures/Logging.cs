namespace Sdk.Common.Tests.Fixtures;

using Microsoft.Extensions.Logging;

public class XUnitLoggerFactory(ITestOutputHelper output) : ILoggerFactory
{
    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(output, categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
        // Not required for this implementation
    }

    private class XUnitLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _categoryName;

        public XUnitLogger(ITestOutputHelper output, string categoryName)
        {
            _output = output;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null; // Noop
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            // You can customize this to filter log levels if needed
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _output.WriteLine($"{logLevel}: {formatter(state, exception)}");
        }
    }
}