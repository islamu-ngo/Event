using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Fixtures;

public sealed class InMemoryEmailDispatchOutboxRepositoryTests
{
    [Test]
    public async Task SuppressEventReminders_MatchingActiveReminder_SkipsAndClearsClaim()
    {
        DateTime changedAt = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        var dispatch = CreateReminder();
        dispatch.Status = EmailDispatchStatus.Processing;
        dispatch.NextAttemptAt = changedAt.AddHours(1);
        dispatch.ProcessingStartedAt = changedAt.AddMinutes(-1);
        dispatch.ProcessingLeaseToken = Guid.CreateVersion7();
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);

        EventReminderStateChangeResult result =
            await repository.SuppressEventRemindersInCurrentTransactionAsync(
                new EventReminderSupersessionRequest(
                    dispatch.TenantId,
                    dispatch.EventId!.Value,
                    dispatch.RegistrationOrderId,
                    SessionId(),
                    changedAt,
                    "event_cancelled"),
                CancellationToken.None);

        await Assert.That(result.OutboxRowsChanged).IsEqualTo(1);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(dispatch.NextAttemptAt).IsNull();
        await Assert.That(dispatch.ProcessingStartedAt).IsNull();
        await Assert.That(dispatch.ProcessingLeaseToken).IsNull();
        await Assert.That(dispatch.LastFailureCategory).IsEqualTo("event_cancelled");
        await Assert.That(dispatch.LastFailureAt).IsEqualTo(changedAt);
    }

    [Test]
    public async Task SuppressEventReminders_ProviderHandoffStarted_DoesNotChangeReminder()
    {
        var dispatch = CreateReminder();
        dispatch.Status = EmailDispatchStatus.Processing;
        dispatch.AttemptCount = 1;
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        repository.Attempts.Add(new EmailDispatchAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = dispatch.TenantId,
            EmailDispatchOutboxId = dispatch.Id,
            AttemptNumber = 1,
            FailureCategory = "provider_handoff_started"
        });

        EventReminderStateChangeResult result =
            await repository.SuppressEventRemindersInCurrentTransactionAsync(
                new EventReminderSupersessionRequest(
                    dispatch.TenantId,
                    dispatch.EventId!.Value,
                    dispatch.RegistrationOrderId,
                    SessionId(),
                    DateTime.UtcNow,
                    "event_cancelled"),
                CancellationToken.None);

        await Assert.That(result.OutboxRowsChanged).IsEqualTo(0);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Processing);
    }

    [Test]
    public async Task RescheduleEventReminders_MatchingReminder_RecalculatesDueTimeAndClearsFailure()
    {
        DateTime changedAt = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
        DateTimeOffset sessionStart = new(changedAt.AddHours(3), TimeSpan.Zero);
        var dispatch = CreateReminder(sessionStart);
        dispatch.Status = EmailDispatchStatus.RetryScheduled;
        dispatch.NextAttemptAt = changedAt.AddMinutes(10);
        dispatch.LastFailureCategory = "temporary";
        dispatch.LastError = "temporary";
        dispatch.LastFailureAt = changedAt.AddMinutes(-1);
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);

        EventReminderStateChangeResult result =
            await repository.RescheduleEventRemindersInCurrentTransactionAsync(
                new EventReminderRescheduleRequest(
                    dispatch.TenantId,
                    dispatch.EventId!.Value,
                    dispatch.RegistrationOrderId,
                    SessionId(),
                    " Updated event ",
                    TimeSpan.FromHours(1),
                    changedAt),
                CancellationToken.None);

        await Assert.That(result.OutboxRowsChanged).IsEqualTo(1);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(dispatch.NextAttemptAt).IsEqualTo(changedAt.AddHours(2));
        await Assert.That(dispatch.Subject).IsEqualTo("Reminder: Updated event");
        await Assert.That(dispatch.LastFailureCategory).IsNull();
        await Assert.That(dispatch.LastError).IsNull();
        await Assert.That(dispatch.LastFailureAt).IsNull();
    }

    [Test]
    public async Task RescheduleEventReminders_NoFutureSession_SuppressesReminder()
    {
        DateTime changedAt = new(2026, 7, 20, 15, 0, 0, DateTimeKind.Utc);
        var dispatch = CreateReminder(new DateTimeOffset(changedAt, TimeSpan.Zero));
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);

        EventReminderStateChangeResult result =
            await repository.RescheduleEventRemindersInCurrentTransactionAsync(
                new EventReminderRescheduleRequest(
                    dispatch.TenantId,
                    dispatch.EventId!.Value,
                    dispatch.RegistrationOrderId,
                    SessionId(),
                    "Event",
                    TimeSpan.FromHours(1),
                    changedAt),
                CancellationToken.None);

        await Assert.That(result.OutboxRowsChanged).IsEqualTo(1);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(dispatch.NextAttemptAt).IsNull();
        await Assert.That(dispatch.LastFailureCategory).IsEqualTo("event_reminder_schedule_changed");
    }

    [Test]
    public async Task SuppressEventReminders_InvalidReason_Throws()
    {
        var dispatch = CreateReminder();
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.SuppressEventRemindersInCurrentTransactionAsync(
                new EventReminderSupersessionRequest(
                    dispatch.TenantId,
                    dispatch.EventId!.Value,
                    dispatch.RegistrationOrderId,
                    SessionId(),
                    DateTime.UtcNow,
                    string.Empty),
                CancellationToken.None));
    }

    [Test]
    public async Task RescheduleEventReminders_Cancelled_Throws()
    {
        var dispatch = CreateReminder();
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repository.RescheduleEventRemindersInCurrentTransactionAsync(
                new EventReminderRescheduleRequest(
                    dispatch.TenantId,
                    dispatch.EventId!.Value,
                    dispatch.RegistrationOrderId,
                    SessionId(),
                    "Event",
                    TimeSpan.FromHours(1),
                    DateTime.UtcNow),
                cancellation.Token));
    }

    private static EmailDispatchOutbox CreateReminder(DateTimeOffset? sessionStart = null)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid registrationOrderId = Guid.CreateVersion7();
        Guid sessionId = SessionId();
        return new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Kind = EmailDispatchKind.EventReminder,
            SourceType = "RegistrationOrder",
            SourceId = registrationOrderId,
            NotificationIntentId = Guid.CreateVersion7(),
            EventId = eventId,
            RegistrationOrderId = registrationOrderId,
            RecipientUserId = Guid.CreateVersion7(),
            RecipientEmail = "recipient@example.test",
            Subject = "Reminder: Event",
            CorrelationId = EventReminderAuthorityReference.Format(
                sessionId,
                sessionStart ?? new DateTimeOffset(2026, 7, 20, 15, 0, 0, TimeSpan.Zero),
                "UTC"),
            Status = EmailDispatchStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Guid SessionId() => Guid.Parse("018e4e5c-7f00-7000-8000-000000000123");
}
