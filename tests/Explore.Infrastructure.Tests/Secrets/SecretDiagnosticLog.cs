using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Secrets;

internal sealed class SecretDiagnosticLog<T> : ILogger<T>
{
    private readonly List<string> _entries = [];
    internal string Output => string.Join("\n", _entries);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add(JsonSerializer.Serialize(new
        {
            Level = logLevel,
            Message = formatter(state, exception),
            State = state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [],
            Exception = exception?.ToString()
        }));
}
