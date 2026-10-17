using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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
    [Arguments(true)]
    [Arguments(false)]
    [Timeout(180_000)]
    public async Task ReplayWorker_PreservesDurableResetAndMissingOutboxParking(bool exists)
    {
        var settings = CreateSettings(rabbitMq);
        await using var database = new PersistedEmailDispatchScenario.DispatchDatabase();
        EmailDispatchPointer pointer;
        await using (var writer = database.CreateContext())
        {
            await writer.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(writer, CancellationToken.None);
            await LookupTableSeeder.SeedAsync(writer, CancellationToken.None);
            await database.SeedAsync(writer, "Replay notification", "Replay storage fixture.");
            var dispatch = await writer.EmailDispatchOutbox.IgnoreQueryFilters().SingleAsync();
            if (exists)
            {
                dispatch.Status = EmailDispatchStatus.DeadLettered;
                dispatch.DeadLetteredAt = DateTime.UtcNow;
                dispatch.LastFailureCategory = "broker_rejected";
                dispatch.LastError = "broker_rejected";
                var delivery = await writer.NotificationDeliveries.IgnoreQueryFilters().SingleAsync();
                delivery.StatusId = (int)NotificationDeliveryStatusEnum.DeadLettered;
                await writer.SaveChangesAsync();
                pointer = EmailDispatchPointer.FromOutbox(dispatch);
            }
            else
            {
                pointer = new(database.TenantId, Guid.CreateVersion7(),
                    EmailDispatchKind.RegistrationConfirmation, "notification_intent", Guid.CreateVersion7(), null, null);
            }
        }
        var services = new ServiceCollection();
        services.AddScoped(_ => database.CreateContext(database.TenantId));
        services.AddScoped<IEmailDispatchOutboxRepository, EmailDispatchOutboxRepository>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await transport.DeclareTopologyAsync(CancellationToken.None);
        await using var broker = await new ConnectionFactory { Uri = new Uri(rabbitMq.AmqpConnectionString) }
            .CreateConnectionAsync();
        await using var channel = await broker.CreateChannelAsync();
        var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new AsyncEventingBasicConsumer(channel);
        observer.ReceivedAsync += (_, args) =>
        {
            delivered.TrySetResult(Encoding.UTF8.GetString(args.Body.Span));
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(exists ? settings.DispatchQueueName : settings.ParkingQueueName,
            autoAck: true, consumerTag: "replay-result-observer", noLocal: false, exclusive: false,
            arguments: null, consumer: observer, cancellationToken: CancellationToken.None);
        using var meter = new Meter("ReplaySettlementTest");
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(meter);
        var metrics = new BusinessMetrics(meterFactory);
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (ReferenceEquals(instrument.Meter, meter)
                && instrument.Name == "explore.email_dispatch.rabbitmq.consumes")
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "outcome" && tag.Value is string outcome
                    && outcome == (exists ? "replayed" : "parked"))
                    acknowledged.TrySetResult();
        });
        listener.Start();
        Task ack = acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Task<string> payload = delivered.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var replay = new EmailDispatchRabbitMqDeadLetterReplayService(
            new ConfigurationBuilder().Build(), settingsMonitor, transport, provider, metrics,
            NullLogger<EmailDispatchRabbitMqDeadLetterReplayService>.Instance);
        await replay.StartAsync(CancellationToken.None);
        try
        {
            await Assert.That(await rabbitMq.PublishStringAsync(settings.DeadLetterExchangeName,
                settings.DeadLetterRoutingKey, JsonSerializer.Serialize(pointer, JsonOptions))).IsTrue();
            await ack;
            using var document = JsonDocument.Parse(await payload);
            await Assert.That(document.RootElement.GetProperty("publishEventId").GetGuid()).IsEqualTo(pointer.PublishEventId);
        }
        finally
        {
            await replay.StopAsync(CancellationToken.None);
            replay.Dispose();
        }

        await using var context = database.CreateContext(database.TenantId);
        var persisted = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(persisted.DeadLetteredAt).IsNull();
        await Assert.That(persisted.LastError).IsNull();
        await Assert.That(persisted.LastFailureCategory).IsNull();
        await Assert.That(persisted.ProcessingLeaseToken).IsNull();
        await Assert.That(await context.EmailDispatchAttempts.CountAsync()).IsEqualTo(0);
        var currentDelivery = await context.NotificationDeliveries.AsNoTracking().SingleAsync();
        await Assert.That(currentDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
        await Assert.That(await channel.BasicGetAsync(settings.DeadLetterQueueName, autoAck: true)).IsNull();
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
