using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<MailpitContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("MailpitSmtp")]
public sealed class EmailDispatchDrainMailpitTests(MailpitContainerFixture mailpit)
{
    [Test]
    [Timeout(180_000)]
    public async Task ProcessBatchAsync_WithPendingOutbox_SendsToMailpitAndPersistsSentState()
    {
        await mailpit.ClearMessagesAsync();
        var subject = $"Registration confirmation {Guid.CreateVersion7():N}";
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.RegistrationConfirmation,
            SourceType = "event-registration",
            SourceId = Guid.CreateVersion7(),
            RecipientEmail = "attendee@example.test",
            Subject = subject,
            PlainTextBody = "Your registration was confirmed by the drain service.",
            HtmlBody = "<p>Your registration was confirmed by the drain service.</p>",
            Status = EmailDispatchStatus.Pending,
            CorrelationId = $"corr-{Guid.CreateVersion7():N}"
        };
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        var service = CreateDrainService(repository, tenantAccessor);

        var result = await service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.PendingCount).IsEqualTo(1);
        await Assert.That(result.ProcessedCount).IsEqualTo(1);
        await Assert.That(result.SentCount).IsEqualTo(1);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Sent);
        await Assert.That(dispatch.SentAt).IsNotNull();
        await Assert.That(dispatch.ProviderMessageId).IsNull();
        await Assert.That(repository.Attempts.Count).IsEqualTo(1);
        await Assert.That(repository.Attempts[0].Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Succeeded);
        await Assert.That(repository.Attempts[0].ProviderMessageId).IsNull();
        await Assert.That(repository.Receipts.Count).IsEqualTo(1);
        await Assert.That(repository.Receipts[0].Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);
        await Assert.That(repository.Receipts[0].ProviderMessageId).IsNull();
        tenantAccessor.Received(1).SetTenant(dispatch.TenantId);
        tenantAccessor.Received(1).Clear();

        var summary = await mailpit.WaitForMessageAsync(
            message => message.Subject == subject
                && message.To.Any(address => address.Address == "attendee@example.test"),
            TimeSpan.FromSeconds(10));
        var detail = await mailpit.GetMessageAsync(summary.Id);

        await Assert.That(summary.From.Address).IsEqualTo("noreply@example.test");
        await Assert.That(detail.Text).Contains("Your registration was confirmed by the drain service.");
        await Assert.That(detail.Html).Contains("Your registration was confirmed by the drain service.");
    }

    [Test]
    [Timeout(180_000)]
    public async Task ProcessSingleAsync_WhenDuplicateConsumersRace_SendsOneMailpitMessageAndKeepsSingleSentReceipt()
    {
        await mailpit.ClearMessagesAsync();
        var subject = $"Duplicate claim confirmation {Guid.CreateVersion7():N}";
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.RegistrationConfirmation,
            SourceType = "event-registration",
            SourceId = Guid.CreateVersion7(),
            RecipientEmail = "attendee@example.test",
            Subject = subject,
            PlainTextBody = "Only one duplicate-claim email should be delivered.",
            HtmlBody = "<p>Only one duplicate-claim email should be delivered.</p>",
            Status = EmailDispatchStatus.Pending,
            CorrelationId = $"corr-{Guid.CreateVersion7():N}"
        };
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        var service = CreateDrainService(repository, Substitute.For<ITenantContextAccessor>());

        var results = await Task.WhenAll(
            service.ProcessSingleAsync(dispatch.TenantId, dispatch.PublishEventId, "consumer-a", CancellationToken.None),
            service.ProcessSingleAsync(dispatch.TenantId, dispatch.PublishEventId, "consumer-b", CancellationToken.None));

        await Assert.That(results.Count(result => result.Outcome == EmailDispatchDrainOutcome.Sent)).IsEqualTo(1);
        await Assert.That(results.Count(result =>
            result.Outcome is EmailDispatchDrainOutcome.AlreadyClaimed or EmailDispatchDrainOutcome.AlreadySettled))
            .IsEqualTo(1);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Sent);
        await Assert.That(repository.Attempts.Count).IsEqualTo(1);
        await Assert.That(repository.Receipts.Count).IsEqualTo(1);
        await Assert.That(repository.Receipts[0].Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);

        await mailpit.WaitForMessageAsync(
            message => message.Subject == subject,
            TimeSpan.FromSeconds(10));
        var messages = await mailpit.GetMessagesAsync(CancellationToken.None);
        await Assert.That(messages.Count(message => message.Subject == subject)).IsEqualTo(1);
    }

    private EmailDispatchDrainService CreateDrainService(
        InMemoryEmailDispatchOutboxRepository repository,
        ITenantContextAccessor tenantAccessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmailDispatchOutboxRepository>(repository);
        services.AddSingleton<IEmailService>(CreateSmtpEmailService());
        services.AddSingleton(Substitute.For<IUserNotificationPreferenceRepository>());
        services.AddSingleton(Substitute.For<IEmailUnsubscribeTokenService>());
        services.AddSingleton(CreateEnabledNotificationPreferenceResolver());
        services.AddSingleton(tenantAccessor);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var serviceProvider = services.BuildServiceProvider();

        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new EmailDispatchDrainService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailDispatchProcessorSettings
            {
                BatchSize = 10,
                ConsumerId = "mailpit-drain-test"
            }),
            new BusinessMetrics(meterFactory),
            NullLogger<EmailDispatchDrainService>.Instance);
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

}
