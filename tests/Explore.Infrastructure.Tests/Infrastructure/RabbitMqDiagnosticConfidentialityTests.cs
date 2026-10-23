using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class RabbitMqDiagnosticConfidentialityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HealthKeepsEnabledAndFailureStateWithoutConfiguredBrokerIdentity(bool enabled)
    {
        var canary = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddOptions<EmailDispatchRabbitMqSettings>().Configure(options =>
        {
            options.Enabled = enabled;
            options.ConnectionStringName = canary;
            options.ExchangeName = canary;
            options.DispatchQueueName = canary;
            options.DeadLetterQueueName = canary;
            options.ParkingQueueName = canary;
        });
        using var provider = services.BuildServiceProvider();
        var logger = new TestListLogger<RabbitMqEmailDispatchTransport>();
        var metrics = new BusinessMetrics(provider.GetRequiredService<IMeterFactory>());
        await using var transport = new RabbitMqEmailDispatchTransport(
            new ConfigurationBuilder().Build(),
            provider.GetRequiredService<IOptionsMonitor<EmailDispatchRabbitMqSettings>>(),
            metrics, logger);

        var health = await transport.CheckHealthAsync();

        await Assert.That(health.Enabled).IsEqualTo(enabled);
        await Assert.That(health.Healthy).IsEqualTo(!enabled);
        await Assert.That(health.Data!.Count).IsEqualTo(1);
        await Assert.That(health.Data["enabled"]).IsEqualTo(enabled);
        await Assert.That(JsonSerializer.Serialize(health)).DoesNotContain(canary);
        await Assert.That(logger.Entries.Count).IsEqualTo(enabled ? 1 : 0);
        foreach (var entry in logger.Entries)
        {
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.Message + JsonSerializer.Serialize(entry.State)).DoesNotContain(canary);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeConsumerFailureSettlesWithoutExportingBrokerExceptionOrConfiguredIdentity(bool replay)
    {
        string canary = Guid.NewGuid().ToString("N") + "\r\nprivate-broker";
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddOptions<EmailDispatchRabbitMqSettings>().Configure(options =>
        {
            options.Enabled = true;
            options.DeadLetterReplayEnabled = true;
            options.ConsumerId = canary;
            options.DeadLetterReplayConsumerId = canary;
            options.DispatchQueueName = canary;
            options.DeadLetterQueueName = canary;
            options.ClientProvidedName = canary;
        });
        using var provider = services.BuildServiceProvider();
        var transport = Substitute.For<IEmailDispatchTransport>();
        transport.DeclareTopologyAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException(canary)));
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exports = new ConcurrentQueue<string>();
        var consumerLogger = new FailureCollector<EmailDispatchRabbitMqConsumerService>(signal, exports);
        var replayLogger = new FailureCollector<EmailDispatchRabbitMqDeadLetterReplayService>(signal, exports);
        var options = provider.GetRequiredService<IOptionsMonitor<EmailDispatchRabbitMqSettings>>();
        var metrics = new BusinessMetrics(provider.GetRequiredService<IMeterFactory>());
        var configuration = new ConfigurationBuilder().Build();
        using BackgroundService service = replay
            ? new EmailDispatchRabbitMqDeadLetterReplayService(configuration, options, transport, provider, metrics, replayLogger)
            : new EmailDispatchRabbitMqConsumerService(configuration, options, transport, provider, metrics, consumerLogger);
        Task errorObserved = signal.Task;

        await service.StartAsync(default);
        await errorObserved.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)));

        await Assert.That(service.ExecuteTask!.IsCompleted).IsTrue();
        await Assert.That(consumerLogger.HasPrefetchTag || replayLogger.HasPrefetchTag).IsTrue();
        await Assert.That(consumerLogger.ErrorCount + replayLogger.ErrorCount).IsEqualTo(1);
        await Assert.That(string.Join(' ', exports)).DoesNotContain(canary[..32]);
    }

    private sealed class FailureCollector<T>(
        TaskCompletionSource signal, ConcurrentQueue<string> exports) : ILogger<T>
    {
        public bool HasPrefetchTag { get; private set; }
        public int ErrorCount { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [];
            HasPrefetchTag |= fields.Any(field => field.Key == "PrefetchCount");
            exports.Enqueue(formatter(state, exception) + JsonSerializer.Serialize(fields) + exception?.ToString());
            if (logLevel == LogLevel.Error)
            {
                ErrorCount++;
                signal.TrySetResult();
            }
        }
    }
}
