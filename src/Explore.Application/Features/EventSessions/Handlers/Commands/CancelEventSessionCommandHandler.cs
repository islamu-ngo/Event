// ABOUTME: Handler that transitions an event session to the Cancelled lifecycle state.
// ABOUTME: Adds an immutable attendee occurrence only when a published session is cancelled.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public sealed class CancelEventSessionCommandHandler(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
    IEventLifecycleScheduler eventLifecycleScheduler,
    TimeProvider timeProvider,
    ISettingMutationLock mutationLock)
    : EventSessionLifecycleTransitionCommandHandlerBase<CancelEventSessionCommand>(
        eventSessionRepository,
        eventRepository,
        unitOfWork,
        cache,
        timeProvider)
{
    protected override Task<BaseCommandResponse<Guid>> ExecuteTransitionAsync(
        Func<CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken) => mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]], operation, cancellationToken);

    protected override string ActionName => "cancel";
    protected override string PastTenseActionName => "cancelled";
    protected override string ConcurrencyFailureCode => "event_session_cancel_concurrency_conflict";
    protected override string InvalidStatusFailureCode => "event_session_cancel_invalid_status";

    protected override TransitionAttempt CreateTransitionAttempt() => new(
        timeProvider.GetUtcNow().UtcDateTime,
        Guid.CreateVersion7(),
        Guid.CreateVersion7());

    protected override bool IsAlreadyApplied(EventSession session) =>
        session.EventSessionStatusId == (int)EventSessionStatusEnum.Cancelled;

    protected override async Task AfterTransitionInCurrentTransactionAsync(
        EventSession session,
        Event parentEvent,
        int previousStatusId,
        Guid expectedConcurrencyStamp,
        TransitionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (previousStatusId != (int)EventSessionStatusEnum.Published)
        {
            return;
        }

        string snapshot = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            parentEvent.Title,
            session.Title,
            session.StartTime,
            session.EndTime,
            parentEvent.GetEffectiveScheduleTimeZoneId(),
            Location: null));
        await fanoutCoordinator.CoordinateInCurrentTransactionAsync(
            new NotificationFanoutOccurrenceCandidate(
                attempt.OccurrenceId,
                attempt.PointerOutboxMessageId,
                session.TenantId,
                parentEvent.Id,
                session.Id,
                attempt.OccurredAt,
                attempt.OccurredAt,
                expectedConcurrencyStamp,
                NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1([
                    NotificationFanoutChangeField.Cancelled])),
                snapshot,
                snapshot,
                NotificationFanoutRecipientTemplateFactory.SessionCancelledTemplateKey,
                NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
                NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                attempt.OccurredAt,
                "event_session_cancel_command",
                session.Id),
            cancellationToken);
        await eventLifecycleScheduler.ReprojectEventRemindersInCurrentTransactionAsync(
            new EventReminderReprojectionInput(
                session.TenantId,
                parentEvent.Id,
                RegistrationOrderId: null,
                session.Id,
                parentEvent.Title,
                attempt.OccurredAt,
                parentEvent.GetEffectiveScheduleTimeZoneId()),
            cancellationToken);
    }

    protected override bool ApplyTransition(EventSession session, EventStatusEnum parentEventStatus, DateTime occurredAt) =>
        session.Cancel(parentEventStatus, occurredAt);
}
