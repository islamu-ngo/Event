using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

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
        var dispatch = CreateDispatch(subject);
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await using var serviceProvider = CreateServiceProvider(repository, tenantAccessor, settings.ConsumerId);
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(),
            settingsMonitor,
            transport,
            serviceProvider,
            CreateMetrics(),
            NullLogger<EmailDispatchRabbitMqConsumerService>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            EmailDispatchPublishResult publishResult = await transport.PublishDispatchPointerAsync(
                EmailDispatchPointer.FromOutbox(dispatch),
                CancellationToken.None);

            await Assert.That(publishResult.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);

            var summary = await mailpit.WaitForMessageAsync(
                message => message.Subject == subject
                    && message.To.Any(address => address.Address == dispatch.RecipientEmail),
                TimeSpan.FromSeconds(20));
            var detail = await mailpit.GetMessageAsync(summary.Id);
            await WaitForQueueSettledAsync(settings.DispatchQueueName, TimeSpan.FromSeconds(10));

            await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Sent);
            await Assert.That(dispatch.SentAt).IsNotNull();
            await Assert.That(dispatch.ProviderMessageId).IsNotNull();
            await Assert.That(repository.Attempts.Count).IsEqualTo(1);
            await Assert.That(repository.Attempts[0].Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Succeeded);
            await Assert.That(repository.Receipts.Count).IsEqualTo(1);
            await Assert.That(repository.Receipts[0].Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);
            await Assert.That(repository.Receipts[0].ConsumerId).IsEqualTo(settings.ConsumerId);
            tenantAccessor.Received(1).SetTenant(dispatch.TenantId);
            tenantAccessor.Received(1).Clear();
            await Assert.That(detail.Text).Contains(dispatch.PlainTextBody);
            await Assert.That(detail.Html).Contains(dispatch.HtmlBody);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Consumer_WithMalformedPointer_RejectsToDeadLetterQueueWithoutSendingMail()
    {
        await mailpit.ClearMessagesAsync();
        var settings = CreateSettings(rabbitMq);
        var repository = new InMemoryEmailDispatchOutboxRepository(CreateDispatch("unused malformed pointer"));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await transport.DeclareTopologyAsync(CancellationToken.None);
        await using var serviceProvider = CreateServiceProvider(repository, tenantAccessor, settings.ConsumerId);
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(),
            settingsMonitor,
            transport,
            serviceProvider,
            CreateMetrics(),
            NullLogger<EmailDispatchRabbitMqConsumerService>.Instance);
        var malformedPayload = $"not-json-{Guid.CreateVersion7():N}";

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var routed = await rabbitMq.PublishStringAsync(
                settings.ExchangeName,
                settings.DispatchRoutingKey,
                malformedPayload);
            var deadLetterPayload = await WaitForDeadLetterPayloadAsync(
                settings.DeadLetterQueueName,
                TimeSpan.FromSeconds(20));
            var messages = await mailpit.GetMessagesAsync(CancellationToken.None);

            await Assert.That(routed).IsTrue();
            await Assert.That(deadLetterPayload).IsEqualTo(malformedPayload);
            await Assert.That(messages.Count).IsEqualTo(0);
            await Assert.That(repository.Attempts.Count).IsEqualTo(0);
            tenantAccessor.DidNotReceive().SetTenant(Arg.Any<Guid>());
            await WaitForQueueSettledAsync(settings.DispatchQueueName, TimeSpan.FromSeconds(10));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Consumer_WithMissingOutbox_RejectsToDeadLetterQueueWithoutSendingMail()
    {
        await mailpit.ClearMessagesAsync();
        var settings = CreateSettings(rabbitMq);
        var repository = new InMemoryEmailDispatchOutboxRepository(CreateDispatch("unused missing outbox"));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await using var serviceProvider = CreateServiceProvider(repository, tenantAccessor, settings.ConsumerId);
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(),
            settingsMonitor,
            transport,
            serviceProvider,
            CreateMetrics(),
            NullLogger<EmailDispatchRabbitMqConsumerService>.Instance);
        var missingPointer = new EmailDispatchPointer(
            TenantId: Guid.CreateVersion7(),
            PublishEventId: Guid.CreateVersion7(),
            Kind: EmailDispatchKind.RegistrationConfirmation,
            SourceType: "event-registration",
            SourceId: Guid.CreateVersion7(),
            EventId: Guid.CreateVersion7(),
            RegistrationOrderId: Guid.CreateVersion7());

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            EmailDispatchPublishResult publishResult = await transport.PublishDispatchPointerAsync(
                missingPointer,
                CancellationToken.None);
            var deadLetterPayload = await WaitForDeadLetterPayloadAsync(
                settings.DeadLetterQueueName,
                TimeSpan.FromSeconds(20));
            var messages = await mailpit.GetMessagesAsync(CancellationToken.None);

            await Assert.That(publishResult.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
            await Assert.That(deadLetterPayload).Contains(missingPointer.PublishEventId.ToString());
            await Assert.That(messages.Count).IsEqualTo(0);
            await Assert.That(repository.Dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
            await Assert.That(repository.Attempts.Count).IsEqualTo(0);
            tenantAccessor.DidNotReceive().SetTenant(Arg.Any<Guid>());
            await WaitForQueueSettledAsync(settings.DispatchQueueName, TimeSpan.FromSeconds(10));
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private async Task<string> WaitForDeadLetterPayloadAsync(string queueName, TimeSpan timeout)
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
            $"RabbitMQ dead-letter queue did not receive a message. Messages={finalQueue.Messages}, Ready={finalQueue.MessagesReady}, Unacknowledged={finalQueue.MessagesUnacknowledged}.");
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
            $"RabbitMQ dispatch queue did not settle. Messages={finalQueue.Messages}, Ready={finalQueue.MessagesReady}, Unacknowledged={finalQueue.MessagesUnacknowledged}.");
    }

    private ServiceProvider CreateServiceProvider(
        InMemoryEmailDispatchOutboxRepository repository,
        ITenantContextAccessor tenantAccessor,
        string consumerId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmailDispatchOutboxRepository>(repository);
        services.AddSingleton<IEmailService>(CreateSmtpEmailService());
        services.AddSingleton(Substitute.For<IUserNotificationPreferenceRepository>());
        services.AddSingleton(Substitute.For<IEmailUnsubscribeTokenService>());
        services.AddSingleton(CreateEnabledNotificationPreferenceResolver());
        services.AddSingleton(tenantAccessor);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IEmailDispatchDrainService>(provider => new EmailDispatchDrainService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailDispatchProcessorSettings
            {
                BatchSize = 10,
                ConsumerId = consumerId
            }),
            CreateMetrics(),
            NullLogger<EmailDispatchDrainService>.Instance));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
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

    private static INotificationPreferenceResolver CreateEnabledNotificationPreferenceResolver()
    {
        var resolver = Substitute.For<INotificationPreferenceResolver>();
        resolver.ResolveAsync(Arg.Any<NotificationPreferenceResolveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<NotificationPreferenceResolveRequest>();
                return new NotificationPreferenceDecision(
                    request.CategoryCode,
                    request.ChannelCode,
                    IsEnabled: true,
                    IsRequired: false,
                    IsLocked: false,
                    IsMuted: false,
                    EffectiveSourceScope: "Default",
                    LockReason: null);
            });

        return resolver;
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

    private static EmailDispatchOutbox CreateDispatch(string subject) => new()
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
        Subject = subject,
        PlainTextBody = "Your registration was confirmed through the RabbitMQ consumer.",
        HtmlBody = "<p>Your registration was confirmed through the RabbitMQ consumer.</p>",
        Status = EmailDispatchStatus.Pending,
        CorrelationId = $"corr-{Guid.CreateVersion7():N}"
    };

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
