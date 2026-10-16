using Serilog;
using MicrosoftLogger = Microsoft.Extensions.Logging.ILogger;

namespace Explore.API.Telemetry;

internal static class ApiDiagnosticLoggingExtensions
{
    /// <summary>
    /// Keeps inherited Microsoft logging scopes outside Serilog so they cannot
    /// replace native event arguments before the diagnostic enricher runs.
    /// </summary>
    internal static IHostBuilder UseApiDiagnosticLogging(this IHostBuilder builder) =>
        builder.UseSerilog(
                (context, services, loggerConfiguration) => loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.With(new ApiDiagnosticLogEnricher())
                    // RequestLoggingMiddleware owns safe HTTP events; framework
                    // request-start/finish messages contain raw URLs before routing.
                    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", Serilog.Events.LogEventLevel.Warning),
                writeToProviders: true)
            .ConfigureServices((_, services) =>
            {
                // UseSerilog replaces Microsoft's LoggerFactory. CaptureScopes
                // therefore cannot enforce this boundary. Decorate its actual
                // factory registration, retaining Serilog's providers and lifetime.
                var registration = services.Last(service => service.ServiceType == typeof(ILoggerFactory));
                var createFactory = registration.ImplementationFactory
                    ?? throw new InvalidOperationException("Serilog must register the API logger factory through a factory.");
                services.Remove(registration);
                services.AddSingleton<ILoggerFactory>(provider =>
                    new ApiDiagnosticLoggerFactory((ILoggerFactory)createFactory(provider)));
            });

    private sealed class ApiDiagnosticLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        public MicrosoftLogger CreateLogger(string categoryName) =>
            new ApiDiagnosticLogger(inner.CreateLogger(categoryName));

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose() => inner.Dispose();
    }

    private sealed class ApiDiagnosticLogger(MicrosoftLogger inner) : MicrosoftLogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
