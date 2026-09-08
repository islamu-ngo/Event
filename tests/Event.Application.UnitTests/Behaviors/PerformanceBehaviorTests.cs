using Explore.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Event.Application.UnitTests.Behaviors;

public class PerformanceBehaviorTests
{
    [Test]
    public async Task SlowRequestLogsSafeMetadataWithoutRequestValues()
    {
        const string token = "token-canary-934db1";
        const string freeText = "private free text canary 8f63c2";
        var userId = Guid.Parse("018f5f3e-8891-7c8e-9242-e719baa0e8d1");
        var tenantId = Guid.Parse("018f5f3e-8891-7c8e-9242-e719baa0e8d2");
        var request = new SensitivePerformanceRequest(token, freeText, userId, tenantId);
        var logger = new CapturingLogger<PerformanceBehavior<SensitivePerformanceRequest, string>>();
        var behavior = new PerformanceBehavior<SensitivePerformanceRequest, string>(logger);
        using var cancellationSource = new CancellationTokenSource();
        CancellationToken observedCancellationToken = default;

        var response = await behavior.Handle(
            request,
            async cancellationToken =>
            {
                observedCancellationToken = cancellationToken;
                await Task.Delay(TimeSpan.FromMilliseconds(525), cancellationToken);
                return "handled";
            },
            cancellationSource.Token);

        var entry = logger.Entries.Single();
        var structuredValues = string.Join(
            "|",
            entry.Properties.Select(property => $"{property.Key}={property.Value}"));

        await Assert.That(response).IsEqualTo("handled");
        await Assert.That(observedCancellationToken).IsEqualTo(cancellationSource.Token);
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Properties.Any(property => ReferenceEquals(property.Value, request))).IsFalse();
        await Assert.That(entry.Message).DoesNotContain(request.ToString());

        foreach (var forbiddenValue in new[] { token, freeText, userId.ToString(), tenantId.ToString() })
        {
            await Assert.That(entry.Message).DoesNotContain(forbiddenValue);
            await Assert.That(structuredValues).DoesNotContain(forbiddenValue);
        }

        var requestType = entry.Properties.Single(property => property.Key == "RequestType").Value;
        var elapsedMilliseconds = entry.Properties.Single(property => property.Key == "ElapsedMilliseconds").Value;

        await Assert.That(requestType).IsEqualTo(nameof(SensitivePerformanceRequest));
        await Assert.That(elapsedMilliseconds).IsTypeOf<long>();
        await Assert.That((long)elapsedMilliseconds!).IsGreaterThan(500);
    }

    private sealed record SensitivePerformanceRequest(
        string Token,
        string FreeText,
        Guid UserId,
        Guid TenantId) : IRequest<string>;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToArray()
                : [];

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
