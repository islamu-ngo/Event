using Explore.Application.Configuration;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services;

public sealed class EventLifecycleScheduler(
    INotificationFanoutOccurrenceRepository fanoutOccurrenceRepository,
    IEmailDispatchOutboxRepository emailDispatchOutboxRepository,
    IOptions<EventReminderOptions> reminderOptions) : IEventLifecycleScheduler
{
    public async Task SuppressEventRemindersInCurrentTransactionAsync(
        EventReminderSuppressionInput request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await fanoutOccurrenceRepository.AcquireEventPrecedenceLockAndHasHeavyAuthorityAsync(
            request.TenantId,
            request.EventId,
            cancellationToken);
        await emailDispatchOutboxRepository.SuppressEventRemindersInCurrentTransactionAsync(
            new EventReminderSupersessionRequest(
                request.TenantId,
                request.EventId,
                request.RegistrationOrderId,
                request.SessionId,
                request.SuppressedAt,
                request.ReasonCode),
            cancellationToken);
    }

    public async Task ReprojectEventRemindersInCurrentTransactionAsync(
        EventReminderReprojectionInput request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        bool hasHeavyAuthority = await fanoutOccurrenceRepository.AcquireEventPrecedenceLockAndHasHeavyAuthorityAsync(
            request.TenantId,
            request.EventId,
            cancellationToken);
        if (hasHeavyAuthority)
        {
            await emailDispatchOutboxRepository.SuppressEventRemindersInCurrentTransactionAsync(
                new EventReminderSupersessionRequest(
                    request.TenantId,
                    request.EventId,
                    RegistrationOrderId: null,
                    request.SessionId,
                    request.ChangedAt.UtcDateTime,
                    "event_reminder_heavy_authority"),
                cancellationToken);
            return;
        }

        await emailDispatchOutboxRepository.RescheduleEventRemindersInCurrentTransactionAsync(
            new EventReminderRescheduleRequest(
                request.TenantId,
                request.EventId,
                request.RegistrationOrderId,
                request.SessionId,
                request.EventTitle,
                reminderOptions.Value.GetValidatedLeadTime(),
                request.ChangedAt.UtcDateTime,
                request.EventTimeZoneId),
            cancellationToken);
    }

    private static void Validate(EventReminderSuppressionInput request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.SuppressedAt.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw new ArgumentException("Reminder suppression requires exact event authority, a UTC time, and a reason.", nameof(request));
        }
    }

    private static void Validate(EventReminderReprojectionInput request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.ChangedAt == default)
        {
            throw new ArgumentException("Reminder reprojection requires exact event authority and a change time.", nameof(request));
        }
    }
}
