using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Telemetry;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.RabbitMQ)]
public sealed class EmailDispatchRabbitMqHealthCheckTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeHealthOmitsConfiguredTopologyAndExceptionContentWithoutOpeningUnconfiguredBroker(bool enabled)
    {
        string canary = Guid.NewGuid().ToString("N");
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
        await using var transport = new RabbitMqEmailDispatchTransport(
            new ConfigurationBuilder().Build(),
            provider.GetRequiredService<IOptionsMonitor<EmailDispatchRabbitMqSettings>>(),
            new BusinessMetrics(provider.GetRequiredService<IMeterFactory>()), logger);

        var health = await transport.CheckHealthAsync();

        await Assert.That(health.Enabled).IsEqualTo(enabled);
        await Assert.That(health.Healthy).IsEqualTo(!enabled);
        await Assert.That(health.Data.Keys).IsEquivalentTo(["enabled"]);
        await Assert.That(health.Data["enabled"]).IsEqualTo(enabled);
        await Assert.That(logger.Entries.Count).IsEqualTo(enabled ? 1 : 0);
        foreach (var entry in logger.Entries)
        {
            await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.Message + JsonSerializer.Serialize(entry.State)).DoesNotContain(canary);
        }
        await Assert.That(JsonSerializer.Serialize(health)).DoesNotContain(canary);
    }

    [Test]
    public async Task CheckHealthAsyncWhenRabbitMqModeDisabledReturnsHealthy()
    {
        var healthCheck = new EmailDispatchRabbitMqHealthCheck(new StubTransport(
            new EmailDispatchTransportHealth(
                Enabled: false,
                Healthy: true,
                Description: "disabled",
                Data: new Dictionary<string, object>
                {
                    ["enabled"] = false,
                    ["connectionStringName"] = "messaging"
                })));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["enabled"]).IsEqualTo(false);
        await Assert.That(result.Data.ContainsKey("password")).IsFalse();
        await Assert.That(result.Data.ContainsKey("secret")).IsFalse();
    }

    [Test]
    public async Task CheckHealthAsyncWhenTransportHealthyReturnsHealthy()
    {
        var healthCheck = new EmailDispatchRabbitMqHealthCheck(new StubTransport(
            new EmailDispatchTransportHealth(
                Enabled: true,
                Healthy: true,
                Description: "broker ready",
                Data: new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["exchange"] = "explore.email-dispatch"
                })));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("broker ready");
        await Assert.That(result.Data["enabled"]).IsEqualTo(true);
        await Assert.That(result.Data["exchange"]).IsEqualTo("explore.email-dispatch");
    }

    [Test]
    public async Task CheckHealthAsyncWhenTransportUnhealthyReturnsUnhealthy()
    {
        var healthCheck = new EmailDispatchRabbitMqHealthCheck(new StubTransport(
            new EmailDispatchTransportHealth(
                Enabled: true,
                Healthy: false,
                Description: "broker unavailable",
                Data: new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["exchange"] = "explore.email-dispatch"
                })));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("broker unavailable");
        await Assert.That(result.Data["exchange"]).IsEqualTo("explore.email-dispatch");
    }

    [Test]
    public async Task DisabledRabbitMqTransportDoesNotOpenInvalidBrokerConnection()
    {
        var settings = new EmailDispatchRabbitMqSettings
        {
            Enabled = false,
            ConnectionString = "not-a-valid-amqp-uri",
            ConnectionStringName = "missing-broker"
        };
        await using var transport = CreateTransport(settings);

        var health = await transport.CheckHealthAsync();

        await Assert.That(health.Enabled).IsFalse();
        await Assert.That(health.Healthy).IsTrue();
        await Assert.That(health.Description).Contains("Basic Dispatch Mode remains independent");
        await Assert.That(health.Data.ContainsKey("connectionStringName")).IsFalse();
    }

    [Test]
    public async Task DisabledRabbitMqTransportPublishReturnsDisabledWithoutBrokerConnection()
    {
        var settings = new EmailDispatchRabbitMqSettings
        {
            Enabled = false,
            ConnectionString = "not-a-valid-amqp-uri"
        };
        await using var transport = CreateTransport(settings);
        var pointer = new EmailDispatchPointer(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Domain.EmailDispatchKind.RegistrationConfirmation,
            "event-registration",
            Guid.CreateVersion7(),
            EventId: Guid.CreateVersion7(),
            RegistrationOrderId: Guid.CreateVersion7());

        EmailDispatchPublishResult result = await transport.PublishDispatchPointerAsync(pointer);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Disabled);
        await Assert.That(result.Succeeded).IsTrue();
    }

    private static RabbitMqEmailDispatchTransport CreateTransport(EmailDispatchRabbitMqSettings settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:missing-broker"] = "not-a-valid-amqp-uri"
            })
            .Build();
        var options = Substitute.For<IOptionsMonitor<EmailDispatchRabbitMqSettings>>();
        options.CurrentValue.Returns(settings);

        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new RabbitMqEmailDispatchTransport(
            configuration,
            options,
            new BusinessMetrics(meterFactory),
            NullLogger<RabbitMqEmailDispatchTransport>.Instance);
    }

    private sealed class StubTransport(EmailDispatchTransportHealth health) : IEmailDispatchTransport
    {
        public Task DeclareTopologyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<EmailDispatchPublishResult> PublishDispatchPointerAsync(
            EmailDispatchPointer pointer,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EmailDispatchPublishResult.Disabled());

        public Task<EmailDispatchTransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(health);
    }
}
