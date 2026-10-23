using System.Text.Json;
using Event.Persistence.IntegrationTests;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Mail.Unsubscribe;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        await using var database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(
            subject, "Your registration was confirmed by the drain service.");
        await using var provider = PersistedEmailDispatchScenario.CreateServiceProvider(database, CreateSmtpEmailService());
        var service = provider.GetRequiredService<EmailDispatchDrainService>();

        var result = await service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.PendingCount).IsEqualTo(1);
        await Assert.That(result.ProcessedCount).IsEqualTo(1);
        await Assert.That(result.SentCount).IsEqualTo(1);
        await PersistedEmailDispatchScenario.AssertSentStateAsync(database);

        var messages = await mailpit.GetMessagesAsync(CancellationToken.None);
        var summary = messages.Single(message => message.Subject == subject
            && message.To.Any(address => address.Address == "attendee@example.test"));
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
        await using var database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(
            subject, "Only one duplicate-claim email should be delivered.");
        var transport = new HeldSmtpEmailService(CreateSmtpEmailService());
        await using var provider = PersistedEmailDispatchScenario.CreateServiceProvider(database, transport);
        var service = provider.GetRequiredService<EmailDispatchDrainService>();
        Task accepted = transport.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Task<EmailDispatchSingleDrainResult> first = service.ProcessSingleAsync(
            database.TenantId, database.PublishEventId, "consumer-a", CancellationToken.None);
        try
        {
            await accepted;
            await using var context = database.CreateContext(database.TenantId);
            var claimed = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
            await Assert.That(claimed.Status).IsEqualTo(EmailDispatchStatus.Processing);
            await Assert.That(claimed.ProcessingLeaseToken).IsNotNull();
            await Assert.That(claimed.AttemptCount).IsEqualTo(1);
            var attempt = await context.EmailDispatchAttempts.AsNoTracking().SingleAsync();
            await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
            await Assert.That(attempt.FailureCategory).IsEqualTo("provider_handoff_started");
            var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync();
            await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Processing);

            var duplicate = await service.ProcessSingleAsync(
                database.TenantId, database.PublishEventId, "consumer-b", CancellationToken.None);
            await Assert.That(duplicate.Outcome).IsEqualTo(EmailDispatchDrainOutcome.AlreadyClaimed);
        }
        finally
        {
            transport.Release.TrySetResult();
            await first;
        }

        await Assert.That((await first).Outcome).IsEqualTo(EmailDispatchDrainOutcome.Sent);
        var replay = await service.ProcessSingleAsync(
            database.TenantId, database.PublishEventId, "consumer-c", CancellationToken.None);
        await Assert.That(replay.Outcome).IsEqualTo(EmailDispatchDrainOutcome.AlreadySettled);
        await PersistedEmailDispatchScenario.AssertSentStateAsync(database);

        var messages = await mailpit.GetMessagesAsync(CancellationToken.None);
        await Assert.That(messages.Count(message => message.Subject == subject)).IsEqualTo(1);
        var summary = messages.Single(message => message.Subject == subject);
        await Assert.That(summary.From.Address).IsEqualTo("noreply@example.test");
        await Assert.That(summary.To.Single().Address).IsEqualTo("attendee@example.test");
        var detail = await mailpit.GetMessageAsync(summary.Id);
        await Assert.That(detail.Text).Contains("Only one duplicate-claim email should be delivered.");
        await Assert.That(detail.Html).Contains("Only one duplicate-claim email should be delivered.");
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

    private sealed class HeldSmtpEmailService(IEmailService smtp) : IEmailService
    {
        public TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            EmailResult result = await smtp.SendAsync(message, cancellationToken);
            if (!result.Success)
            {
                Accepted.TrySetException(new InvalidOperationException("Mailpit SMTP did not accept the message."));
                return result;
            }

            Accepted.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

}
