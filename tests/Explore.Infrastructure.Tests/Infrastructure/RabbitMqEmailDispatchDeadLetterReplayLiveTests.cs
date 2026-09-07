using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.RabbitMQ)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<RabbitMqContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("RabbitMqBroker")]
public sealed class RabbitMqEmailDispatchDeadLetterReplayLiveTests(RabbitMqContainerFixture rabbitMq)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(180_000)]
    public async Task ReplayWorker_WithDeadLetteredOutbox_ResetsDurableRowAndRepublishesPointer()
    {
        var settings = CreateSettings(rabbitMq);
        var dispatch = CreateDispatch(EmailDispatchStatus.DeadLettered);
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        var pointer = EmailDispatchPointer.FromOutbox(dispatch);
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await transport.DeclareTopologyAsync(CancellationToken.None);
        await using var serviceProvider = CreateServiceProvider(repository);
        var replayService = CreateReplayService(settingsMonitor, transport, serviceProvider);

        await replayService.StartAsync(CancellationToken.None);
        try
        {
            var routed = await rabbitMq.PublishStringAsync(
                settings.DeadLetterExchangeName,
                settings.DeadLetterRoutingKey,
                SerializePointer(pointer));
            var replayedPayload = await WaitForQueuePayloadAsync(
                settings.DispatchQueueName,
                TimeSpan.FromSeconds(20));
            await WaitForQueueSettledAsync(settings.DeadLetterQueueName, TimeSpan.FromSeconds(10));

            await Assert.That(routed).IsTrue();
            await Assert.That(replayedPayload).Contains(dispatch.PublishEventId.ToString());
            await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
            await Assert.That(dispatch.DeadLetteredAt).IsNull();
            await Assert.That(repository.ReplayCount).IsEqualTo(1);
        }
        finally
        {
            await replayService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task ReplayWorker_WithMissingOutbox_ParksPayloadAndAcksDeadLetter()
    {
        var settings = CreateSettings(rabbitMq);
        var repository = new InMemoryEmailDispatchOutboxRepository(CreateDispatch(EmailDispatchStatus.Pending));
        var pointer = new EmailDispatchPointer(
            TenantId: Guid.CreateVersion7(),
            PublishEventId: Guid.CreateVersion7(),
            Kind: EmailDispatchKind.RegistrationConfirmation,
            SourceType: "event-registration",
            SourceId: Guid.CreateVersion7(),
            EventId: Guid.CreateVersion7(),
            RegistrationOrderId: Guid.CreateVersion7());
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await transport.DeclareTopologyAsync(CancellationToken.None);
        await using var serviceProvider = CreateServiceProvider(repository);
        var replayService = CreateReplayService(settingsMonitor, transport, serviceProvider);

        await replayService.StartAsync(CancellationToken.None);
        try
        {
            var routed = await rabbitMq.PublishStringAsync(
                settings.DeadLetterExchangeName,
                settings.DeadLetterRoutingKey,
                SerializePointer(pointer));
            var parkedPayload = await WaitForQueuePayloadAsync(
                settings.ParkingQueueName,
                TimeSpan.FromSeconds(20));
            await WaitForQueueSettledAsync(settings.DeadLetterQueueName, TimeSpan.FromSeconds(10));

            await Assert.That(routed).IsTrue();
            await Assert.That(parkedPayload).Contains(pointer.PublishEventId.ToString());
            await Assert.That(repository.Dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
            await Assert.That(repository.ReplayCount).IsEqualTo(0);
        }
        finally
        {
            await replayService.StopAsync(CancellationToken.None);
        }
    }

    private async Task<string> WaitForQueuePayloadAsync(string queueName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var messages = await rabbitMq.GetQueueMessagesAsync(queueName, count: 1);
            if (messages.Count > 0)
            {
                return messages[0].Payload;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        var finalQueue = await rabbitMq.GetQueueAsync(queueName);
        throw new TimeoutException(
            $"RabbitMQ queue did not receive a message. Queue={queueName}, Messages={finalQueue.Messages}, Ready={finalQueue.MessagesReady}, Unacknowledged={finalQueue.MessagesUnacknowledged}.");
    }

    private async Task WaitForQueueSettledAsync(string queueName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var queue = await rabbitMq.GetQueueAsync(queueName);
            if (queue.Messages == 0 && queue.MessagesReady == 0 && queue.MessagesUnacknowledged == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        var finalQueue = await rabbitMq.GetQueueAsync(queueName);
        throw new TimeoutException(
            $"RabbitMQ queue did not settle. Queue={queueName}, Messages={finalQueue.Messages}, Ready={finalQueue.MessagesReady}, Unacknowledged={finalQueue.MessagesUnacknowledged}.");
    }

    private static EmailDispatchRabbitMqDeadLetterReplayService CreateReplayService(
        IOptionsMonitor<EmailDispatchRabbitMqSettings> settingsMonitor,
        IEmailDispatchTransport transport,
        IServiceProvider serviceProvider) =>
        new(
            new ConfigurationBuilder().Build(),
            settingsMonitor,
            transport,
            serviceProvider,
            CreateMetrics(),
            NullLogger<EmailDispatchRabbitMqDeadLetterReplayService>.Instance);

    private static ServiceProvider CreateServiceProvider(InMemoryEmailDispatchOutboxRepository repository)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmailDispatchOutboxRepository>(repository);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static RabbitMqEmailDispatchTransport CreateTransport(
        IOptionsMonitor<EmailDispatchRabbitMqSettings> settingsMonitor) =>
        new(
            new ConfigurationBuilder().Build(),
            settingsMonitor,
            CreateMetrics(),
            NullLogger<RabbitMqEmailDispatchTransport>.Instance);

    private static IOptionsMonitor<EmailDispatchRabbitMqSettings> CreateOptionsMonitor(
        EmailDispatchRabbitMqSettings settings)
    {
        var monitor = Substitute.For<IOptionsMonitor<EmailDispatchRabbitMqSettings>>();
        monitor.CurrentValue.Returns(settings);
        return monitor;
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private static string SerializePointer(EmailDispatchPointer pointer) =>
        JsonSerializer.Serialize(pointer, JsonOptions);

    private static EmailDispatchOutbox CreateDispatch(EmailDispatchStatus status) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        PublishEventId = Guid.CreateVersion7(),
        Kind = EmailDispatchKind.RegistrationConfirmation,
        SourceType = "event-registration",
        SourceId = Guid.CreateVersion7(),
        EventId = Guid.CreateVersion7(),
        RegistrationOrderId = Guid.CreateVersion7(),
        RecipientUserId = Guid.CreateVersion7(),
        RecipientEmail = "attendee@example.test",
        Subject = "Registration confirmation",
        PlainTextBody = "This row is used only for DLQ replay tests.",
        HtmlBody = "<p>This row is used only for DLQ replay tests.</p>",
        Status = status,
        DeadLetteredAt = status == EmailDispatchStatus.DeadLettered ? DateTime.UtcNow : null,
        LastFailureCategory = status == EmailDispatchStatus.DeadLettered ? "broker_rejected" : null,
        LastError = status == EmailDispatchStatus.DeadLettered ? "Message was dead-lettered by broker." : null,
        CorrelationId = $"corr-{Guid.CreateVersion7():N}"
    };

    private static EmailDispatchRabbitMqSettings CreateSettings(RabbitMqContainerFixture rabbitMq)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        return new EmailDispatchRabbitMqSettings
        {
            Enabled = true,
            DeadLetterReplayEnabled = true,
            ConnectionString = rabbitMq.AmqpConnectionString,
            ExchangeName = $"test.email-dispatch.replay.{suffix}",
            DispatchQueueName = $"test.email-dispatch.replay.dispatch.{suffix}",
            DispatchRoutingKey = $"test.email-dispatch.replay.dispatch.{suffix}",
            DeadLetterExchangeName = $"test.email-dispatch.replay.dlx.{suffix}",
            DeadLetterQueueName = $"test.email-dispatch.replay.dead-letter.{suffix}",
            DeadLetterRoutingKey = $"test.email-dispatch.replay.dead-letter.{suffix}",
            ParkingQueueName = $"test.email-dispatch.replay.parking.{suffix}",
            ParkingRoutingKey = $"test.email-dispatch.replay.parking.{suffix}",
            ClientProvidedName = $"test-email-dispatch-replay-{suffix}",
            ConsumerId = $"test-email-dispatch-consumer-{suffix}",
            DeadLetterReplayConsumerId = $"test-email-dispatch-replay-{suffix}",
            DeadLetterReplayPrefetchCount = 1,
            PublishTimeoutSeconds = 5
        };
    }
}
