using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Infrastructure;

internal sealed class TestListLogger<T> : ILogger<T>
{
    public List<TestLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => NoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var structuredState = state is IEnumerable<KeyValuePair<string, object?>> properties
            ? properties.ToArray()
            : [];
        Entries.Add(new TestLogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            exception,
            structuredState));
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record TestLogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> State)
{
    public IReadOnlyList<object?> Arguments =>
        State.Where(property => property.Key != "{OriginalFormat}")
            .Select(property => property.Value)
            .ToArray();
}
