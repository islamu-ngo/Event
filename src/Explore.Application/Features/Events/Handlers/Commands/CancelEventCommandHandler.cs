// ABOUTME: Handler that transitions an event to the Cancelled lifecycle state.
// ABOUTME: Atomically persists the lifecycle change and durable attendee-notification occurrence.

using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Lifecycle;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class CancelEventCommandHandler(
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    IUserContext userContext,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
    IEventLifecycleScheduler eventLifecycleScheduler,
    IRefundCampaignRepository refundCampaignRepository,
    IOutboxRepository outboxRepository,
    TimeProvider timeProvider,
    ISettingMutationLock mutationLock) : IRequestHandler<CancelEventCommand, BaseCommandResponse<Guid>>
{
    private const string ConcurrencyConflictCode = "event_cancel_concurrency_conflict";
    private const string TransitionNotAllowedCode = "event_cancel_transition_not_allowed";
    private const string FanoutSourceType = "event_cancel_command";

    public async Task<BaseCommandResponse<Guid>> Handle(CancelEventCommand request, CancellationToken cancellationToken)
    {
        var validator = new CancelEventRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(request.Id, "Event cancel request is invalid.", validationResult.Errors.Select(e => e.ErrorMessage));
        }

        var @event = await eventRepository.GetById(request.Id);
        if (@event is null)
        {
            return Failure(request.Id, "Event was not found.", new[] { "Event was not found." });
        }

        EventStatusEnum currentStatus = (EventStatusEnum)@event.EventStatusId;
        if (currentStatus == EventStatusEnum.Cancelled)
        {
            return Success(@event.Id, "Event is already cancelled.");
        }

        if (@event.ConcurrencyStamp != request.Request.ExpectedConcurrencyStamp)
        {
            return Failure(request.Id, "Event was modified by another user.", new[] { "The event was modified by another user. Refresh and try again." }, ConcurrencyConflictCode);
        }

        if (!EventLifecycleRules.CanTransition(currentStatus, EventStatusEnum.Cancelled))
        {
            return Failure(request.Id, "Event cannot be cancelled from its current status.", new[] { "Event cannot be cancelled from its current status." }, TransitionNotAllowedCode);
        }

        Guid currentUserId = userContext.GetRequiredUserId();
        Guid federationOutboxId = Guid.CreateVersion7();
        Guid occurrenceId = Guid.CreateVersion7();
        Guid pointerOutboxMessageId = Guid.CreateVersion7();
        Guid refundCampaignId = Guid.CreateVersion7();
        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        Guid? tenantIdToInvalidate = null;
        bool mutationAttempted = false;

        BaseCommandResponse<Guid> response = await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            policyToken => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            tenantIdToInvalidate = null;
            var attemptEvent = await eventRepository.GetById(request.Id);
            if (attemptEvent is null)
                return Failure(request.Id, "Event was not found.", new[] { "Event was not found." });

            EventStatusEnum attemptStatus = (EventStatusEnum)attemptEvent.EventStatusId;
            if (mutationAttempted && attemptStatus == EventStatusEnum.Cancelled)
            {
                tenantIdToInvalidate = attemptEvent.TenantId;
                return Success(attemptEvent.Id, "Event cancelled successfully.");
            }

            if (attemptEvent.ConcurrencyStamp != request.Request.ExpectedConcurrencyStamp)
            {
                return Failure(request.Id, "Event was modified by another user.", new[] { "The event was modified by another user. Refresh and try again." }, ConcurrencyConflictCode);
            }

            if (!EventLifecycleRules.CanTransition(attemptStatus, EventStatusEnum.Cancelled))
            {
                return Failure(request.Id, "Event cannot be cancelled from its current status.", new[] { "Event cannot be cancelled from its current status." }, TransitionNotAllowedCode);
            }

            attemptEvent.Cancel(occurredAt);
            mutationAttempted = true;

            await eventRepository.Update(attemptEvent);
            RefundCampaign refundCampaign = RefundCampaign.CreateCancellation(
                refundCampaignId,
                attemptEvent.TenantId,
                attemptEvent.Id,
                currentUserId,
                "Organizer cancelled the event.",
                occurredAt);
            await refundCampaignRepository.CreateAsync(
                refundCampaign,
                RefundOutboxMessageFactory.CreateCampaignProcess(refundCampaign, occurredAt),
                token);
            await outboxRepository.Create(
                AdmissionRevocationOutboxMessageFactory.CreateEventCancellation(
                    attemptEvent.TenantId,
                    attemptEvent.Id,
                    occurredAt));
            await atprotoPublicationPlanner.PlanEventAsync(
                new AtprotoEventPublicationInput(
                    attemptEvent.TenantId,
                    currentUserId,
                    attemptEvent.Id,
                    attemptEvent.ConcurrencyStamp,
                    PdsSyncOperation.Update,
                    federationOutboxId,
                    occurredAt),
                token);

            string snapshot = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
                attemptEvent.Title,
                SessionTitle: null,
                StartsAt: null,
                EndsAt: null,
                Timezone: null,
                Location: null));
            await fanoutCoordinator.CoordinateInCurrentTransactionAsync(
                new NotificationFanoutOccurrenceCandidate(
                    occurrenceId,
                    pointerOutboxMessageId,
                    attemptEvent.TenantId,
                    attemptEvent.Id,
                    SessionId: null,
                    occurredAt,
                    occurredAt,
                    request.Request.ExpectedConcurrencyStamp,
                    NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1([
                        NotificationFanoutChangeField.Cancelled])),
                    snapshot,
                    snapshot,
                    NotificationFanoutRecipientTemplateFactory.EventCancelledTemplateKey,
                    NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                    (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
                    NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                    occurredAt,
                    FanoutSourceType,
                    attemptEvent.Id),
                token);
            await eventLifecycleScheduler.SuppressEventRemindersInCurrentTransactionAsync(
                new EventReminderSuppressionInput(
                    attemptEvent.TenantId,
                    attemptEvent.Id,
                    RegistrationOrderId: null,
                    SessionId: null,
                    occurredAt,
                    "event_cancelled"),
                token);
            tenantIdToInvalidate = attemptEvent.TenantId;
            return Success(attemptEvent.Id, "Event cancelled successfully.");
        }, policyToken), cancellationToken);

        if (!response.IsSuccess || !tenantIdToInvalidate.HasValue)
        {
            return response;
        }

        await cache.RemoveAsync($"event:detail:{request.Id}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantIdToInvalidate.Value), cancellationToken);
        return response;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);
}
