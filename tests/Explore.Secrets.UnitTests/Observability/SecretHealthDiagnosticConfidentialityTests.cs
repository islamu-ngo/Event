using System.Text.Json;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Observability;

public sealed class SecretHealthDiagnosticConfidentialityTests
{
    [Test]
    public async Task ProviderFailureExportsUnavailableCategoryWithoutPrivateExceptionTypeOrContent()
    {
        var canary = $"{Guid.NewGuid():N}\r\nforged-health-event";
        var provider = Substitute.For<ISecretProvider>();
        provider.ProviderType.Returns(SecretProviderType.Infisical);
        provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProviderHealthInfo>(new PrivateProviderException(canary)));
        var logger = new Collector();
        var check = new SecretProviderHealthCheck(provider, logger);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Data["provider"]).IsEqualTo("Infisical");
        await Assert.That(result.Data["failureCategory"]).IsEqualTo("provider_unavailable");
        await Assert.That(logger.Exports.Count).IsEqualTo(1);
        var finalOutput = JsonSerializer.Serialize(result.Data) + result.Description + logger.Exports.Single();
        await Assert.That(finalOutput.Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
        await Assert.That(finalOutput.Contains(nameof(PrivateProviderException), StringComparison.Ordinal)).IsFalse();
        await Assert.That(logger.Exports.Single().Contains("secret_provider_health_unavailable", StringComparison.Ordinal)).IsTrue();
    }

    private sealed class PrivateProviderException(string message) : Exception(message);

    private sealed class Collector : ILogger<SecretProviderHealthCheck>
    {
        public List<string> Exports { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Exports.Add(JsonSerializer.Serialize(new
            {
                Level = logLevel,
                Message = formatter(state, exception),
                State = state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [],
                Exception = exception?.ToString()
            }));
    }
}
