using Explore.Secrets.Abstractions;
using Explore.Secrets.Providers;
using Explore.Secrets.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

public sealed class SecretAuditSinkConfidentialityTests
{
    [Test]
    public async Task EnabledAuditSinkOmitsPrivateFieldsAndForgedCorrelation()
    {
        var logger = new AuditLogger<StructuredSecretAuditLogger>();
        var audit = new StructuredSecretAuditLogger(logger);
        var canary = $"{Guid.NewGuid():N}@example.invalid\r\nforged-event";
        var entry = new SecretAuditEntry(SecretOperation.RefreshFailed, SecretProviderType.Infisical,
            canary, DateTimeOffset.UtcNow, canary, canary, Success: false, ErrorMessage: canary);

        await audit.LogAsync(entry);

        await Assert.That(logger.Events.Count).IsEqualTo(1);
        var recorded = logger.Events.Single();
        await Assert.That(recorded.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(recorded.Id.Id).IsEqualTo((int)SecretOperation.RefreshFailed);
        await Assert.That(recorded.Exception).IsNull();
        await Assert.That(recorded.Text).DoesNotContain(canary);
        await Assert.That(recorded.Text).DoesNotContain("\r");
        await Assert.That(recorded.Text).DoesNotContain("\n");
        await Assert.That(recorded.Fields["ReasonCode"]).IsEqualTo("secret_provider_refresh_failed");
        await Assert.That(recorded.Fields["CorrelationId"]).IsEqualTo("none");
        await Assert.That(recorded.Fields.Keys).DoesNotContain("KeyPattern");
        await Assert.That(recorded.Fields.Keys).DoesNotContain("UserId");
        await Assert.That(recorded.Fields.Keys).DoesNotContain("ErrorMessage");
        foreach (var field in recorded.Fields.Values)
            await Assert.That(field?.ToString() ?? string.Empty).DoesNotContain(canary);
    }

    [Test]
    public async Task AuditSinkKeepsValidCorrelationAndBoundsUnknownEnums()
    {
        var logger = new AuditLogger<StructuredSecretAuditLogger>();
        var audit = new StructuredSecretAuditLogger(logger);
        var correlation = Guid.NewGuid();
        audit.Log(new SecretAuditEntry((SecretOperation)int.MaxValue,
            (SecretProviderType)int.MaxValue, null, DateTimeOffset.UtcNow,
            CorrelationId: correlation.ToString("D"), Success: true));

        var recorded = logger.Events.Single();
        await Assert.That(recorded.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(recorded.Id.Id).IsEqualTo(-1);
        await Assert.That(recorded.Fields["Operation"]).IsEqualTo("Unknown");
        await Assert.That(recorded.Fields["ProviderType"]).IsEqualTo("Unknown");
        await Assert.That(recorded.Fields["CorrelationId"]).IsEqualTo(correlation.ToString("N"));
    }

    [Test]
    public async Task ProviderDecoratorRejectsForgedHeadersBeforeItsOwnLogs()
    {
        var provider = Substitute.For<ISecretProvider>();
        provider.ProviderType.Returns(SecretProviderType.Infisical);
        provider.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var providerLogger = new AuditLogger<AuditingSecretProviderDecorator>();
        var auditLogger = new AuditLogger<StructuredSecretAuditLogger>();
        var canary = $"{Guid.NewGuid():N}@example.invalid\r\nforged-event";
        var callerGuid = Guid.NewGuid().ToString("N");
        var serverCorrelation = Guid.NewGuid().ToString("N");
        var context = new DefaultHttpContext { TraceIdentifier = canary };
        context.Request.Headers["X-Correlation-Id"] = canary;
        context.Request.Headers["X-Request-Id"] = callerGuid;
        context.Items["CorrelationId"] = serverCorrelation;
        var decorator = new AuditingSecretProviderDecorator(provider,
            new StructuredSecretAuditLogger(auditLogger), providerLogger,
            new HttpContextAccessor { HttpContext = context });

        await decorator.InitializeAsync();

        await Assert.That(providerLogger.Events.Count).IsEqualTo(2);
        await Assert.That(auditLogger.Events.Count).IsEqualTo(1);
        foreach (var recorded in providerLogger.Events.Concat(auditLogger.Events))
        {
            await Assert.That(recorded.Text).DoesNotContain(canary);
            await Assert.That(recorded.Text).DoesNotContain(callerGuid);
            await Assert.That(recorded.Exception).IsNull();
            await Assert.That(Guid.TryParse(recorded.Fields["CorrelationId"]?.ToString(), out _)).IsTrue();
            await Assert.That(recorded.Fields["CorrelationId"]).IsEqualTo(serverCorrelation);
        }
    }

    private sealed class AuditLogger<T> : ILogger<T>
    {
        public List<RecordedEvent> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Events.Add(new RecordedEvent(logLevel, eventId, formatter(state, exception), exception,
                ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .ToDictionary(field => field.Key, field => field.Value)));
    }

    private sealed record RecordedEvent(LogLevel Level, EventId Id, string Text, Exception? Exception,
        Dictionary<string, object?> Fields);
}
