using System.Diagnostics.Metrics;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
[Category(InfrastructureTestCategories.RabbitMQ)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<RabbitMqContainerFixture, MailpitContainerFixture>(Shared = [SharedType.PerClass, SharedType.PerClass])]
[NotInParallel("RabbitMqBroker")]
public sealed class RabbitMqEmailDispatchConsumerMailpitTests(
    RabbitMqContainerFixture rabbitMq,
    MailpitContainerFixture mailpit)
{
    [Test]
    [Timeout(180_000)]
    public async Task Consumer_WithValidPointer_DrainsToMailpitAndAcksAfterDurableOutcome()
    {
        await mailpit.ClearMessagesAsync();
        var settings = CreateSettings(rabbitMq);
        var subject = $"RabbitMQ registration confirmation {Guid.CreateVersion7():N}";
        const string body = "Your registration was confirmed through the RabbitMQ consumer.";
        await using var database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(subject, body);
        await using var provider = PersistedEmailDispatchScenario.CreateServiceProvider(
            database, CreateSmtpEmailService(), settings.ConsumerId);
        await using var context = database.CreateContext(database.TenantId);
        var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        var observer = new SettlementObserver();
        Task settled = observer.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(), settingsMonitor, transport, provider,
            CreateMetrics(), observer);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var published = await transport.PublishDispatchPointerAsync(
                EmailDispatchPointer.FromOutbox(dispatch), CancellationToken.None);
            await Assert.That(published.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
            await settled;
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
            consumer.Dispose();
        }

        await PersistedEmailDispatchScenario.AssertSentStateAsync(database);
        var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync();
        await Assert.That(receipt.ConsumerId).IsEqualTo(settings.ConsumerId);
        await using var broker = await new ConnectionFactory { Uri = new Uri(rabbitMq.AmqpConnectionString) }
            .CreateConnectionAsync();
        await using var channel = await broker.CreateChannelAsync();
        await Assert.That(await channel.BasicGetAsync(settings.DispatchQueueName, autoAck: true)).IsNull();
        var messages = await mailpit.GetMessagesAsync(CancellationToken.None);
        var summary = messages.Single(message => message.Subject == subject);
        await Assert.That(summary.To.Single().Address).IsEqualTo(dispatch.RecipientEmail);
        var detail = await mailpit.GetMessageAsync(summary.Id);
        await Assert.That(detail.Text).Contains(body);
        await Assert.That(detail.Html).Contains($"<p>{body}</p>");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [Timeout(180_000)]
    public async Task Consumer_WithMalformedOrMissingPointer_RejectsToDeadLetterWithoutSendingMail(bool malformed)
    {
        await mailpit.ClearMessagesAsync();
        var settings = CreateSettings(rabbitMq);
        await using var database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(
            "Unclaimed notification", "This notification must not be delivered.");
        await using var provider = PersistedEmailDispatchScenario.CreateServiceProvider(
            database, CreateSmtpEmailService(), settings.ConsumerId);
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await transport.DeclareTopologyAsync(CancellationToken.None);
        await using var broker = await new ConnectionFactory { Uri = new Uri(rabbitMq.AmqpConnectionString) }
            .CreateConnectionAsync();
        await using var channel = await broker.CreateChannelAsync();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadLetters = new AsyncEventingBasicConsumer(channel);
        deadLetters.ReceivedAsync += (_, args) =>
        {
            received.TrySetResult(Encoding.UTF8.GetString(args.Body.Span));
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(settings.DeadLetterQueueName, autoAck: true,
            consumerTag: "mailpit-dead-letter-observer", noLocal: false, exclusive: false,
            arguments: null, consumer: deadLetters, cancellationToken: CancellationToken.None);
        Task<string> delivered = received.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(), settingsMonitor, transport, provider,
            CreateMetrics(), NullLogger<EmailDispatchRabbitMqConsumerService>.Instance);
        string malformedPayload = $"not-json-{Guid.CreateVersion7():N}";
        var missing = new EmailDispatchPointer(Guid.CreateVersion7(), Guid.CreateVersion7(),
            EmailDispatchKind.RegistrationConfirmation, "notification_intent", Guid.CreateVersion7(), null, null);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            if (malformed)
            {
                await Assert.That(await rabbitMq.PublishStringAsync(
                    settings.ExchangeName, settings.DispatchRoutingKey, malformedPayload)).IsTrue();
            }
            else
            {
                var published = await transport.PublishDispatchPointerAsync(missing, CancellationToken.None);
                await Assert.That(published.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
            }
            string payload = await delivered;
            if (malformed)
                await Assert.That(payload).IsEqualTo(malformedPayload);
            else
                await Assert.That(payload).Contains(missing.PublishEventId.ToString());
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
            consumer.Dispose();
        }

        await using var context = database.CreateContext(database.TenantId);
        var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(dispatch.AttemptCount).IsEqualTo(0);
        await Assert.That(dispatch.ProcessingLeaseToken).IsNull();
        await Assert.That(await context.EmailDispatchAttempts.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await mailpit.GetMessagesAsync(CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That(await channel.BasicGetAsync(settings.DispatchQueueName, autoAck: true)).IsNull();
    }

    private sealed class SettlementObserver : ILogger<EmailDispatchRabbitMqConsumerService>
    {
        internal TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var values = fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (values.GetValueOrDefault("BrokerAction") is EmailDispatchRabbitMqSettlementAction.Ack
                && values.GetValueOrDefault("DrainOutcome") is EmailDispatchDrainOutcome.Sent)
                Acknowledged.TrySetResult();
        }
    }

    private SmtpEmailService CreateSmtpEmailService()
    {
        var resolver = Substitute.For<ISmtpConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new SmtpConfiguration
        {
            Host = mailpit.SmtpHost,
            Port = mailpit.SmtpPort,
            Security = SmtpSecurityMode.None,
            FromAddress = "noreply@example.test",
            FromName = "ISLAMU Event Tests",
            TimeoutSeconds = 10
        });

        return new SmtpEmailService(resolver, NullLogger<SmtpEmailService>.Instance);
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
            ConnectionString = rabbitMq.AmqpConnectionString,
            ExchangeName = $"test.email-dispatch.consumer.{suffix}",
            DispatchQueueName = $"test.email-dispatch.consumer.dispatch.{suffix}",
            DispatchRoutingKey = $"test.email-dispatch.consumer.dispatch.{suffix}",
            DeadLetterExchangeName = $"test.email-dispatch.consumer.dlx.{suffix}",
            DeadLetterQueueName = $"test.email-dispatch.consumer.dead-letter.{suffix}",
            DeadLetterRoutingKey = $"test.email-dispatch.consumer.dead-letter.{suffix}",
            ParkingQueueName = $"test.email-dispatch.consumer.parking.{suffix}",
            ParkingRoutingKey = $"test.email-dispatch.consumer.parking.{suffix}",
            ClientProvidedName = $"test-email-dispatch-consumer-{suffix}",
            ConsumerId = $"test-email-dispatch-consumer-{suffix}",
            PrefetchCount = 1,
            PublishTimeoutSeconds = 5
        };
    }
}
