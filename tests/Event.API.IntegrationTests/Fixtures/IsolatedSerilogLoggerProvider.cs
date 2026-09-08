using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed class IsolatedSerilogLoggerProvider(Serilog.ILogger logger) : ILoggerProvider
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        new BridgeLogger(logger.ForContext(Constants.SourceContextPropertyName, categoryName));

    public void Dispose() { }

    private sealed class BridgeLogger(Serilog.ILogger logger) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(ToSerilogLevel(logLevel));

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LogEventLevel level = ToSerilogLevel(logLevel);
            if (!logger.IsEnabled(level))
            {
                return;
            }

            Serilog.ILogger contextual = logger;
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    if (property.Key != "{OriginalFormat}")
                    {
                        contextual = contextual.ForContext(property.Key, property.Value, destructureObjects: false);
                    }
                }
            }

            contextual.Write(level, exception, "{RenderedMessage}", formatter(state, exception));
        }

        private static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }
}
