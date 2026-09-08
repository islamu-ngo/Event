// ABOUTME: Executes ordinary and privileged event publication through one transactional lifecycle path.
// ABOUTME: Privileged approval bypasses only the approval-required gate after authorization has succeeded.

using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Lifecycle;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events;

public enum EventPublicationMode
{
    Ordinary,
    PrivilegedApproval
}

public sealed class EventPublicationExecutor(
    IEventRepository eventRepository,
    IEventLocationRepository eventLocationRepository,
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    IEventLifecyclePolicyProvider policyProvider,
    IEventLifecycleReadinessEvaluator readinessEvaluator,
    IUserContext userContext,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    TimeProvider timeProvider,
    ISettingMutationLock mutationLock,
    IVisitorAccessCapabilityResolver visitorCapabilities)
{
    public const string ConcurrencyConflictCode = "event_publish_concurrency_conflict";
    public const string ReadinessFailedCode = "event_publish_readiness_failed";
    public const string ApprovalRequiredCode = "event_publish_approval_required";

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        Guid eventId,
        PublishEventRequestDto request,
        EventPublicationMode mode,
        CancellationToken cancellationToken)
    {
        var validator = new PublishEventRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                eventId,
                "Event publish request is invalid.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        Guid federationOutboxId = Guid.CreateVersion7();
        Guid notificationFanoutOutboxId = Guid.CreateVersion7();
        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        DateTime federationCreatedAt = occurredAt;
        Guid? tenantIdToInvalidate = null;
        bool mutationAttempted = false;

        BaseCommandResponse<Guid> response = await mutationLock.ExecuteOrderedGroupsAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            outerToken => unitOfWork.ExecuteSerializableAsync(async token =>
        {
            tenantIdToInvalidate = null;
            var attemptEvent = await eventRepository.GetById(eventId);
            if (attemptEvent is null)
            {
                return Failure(eventId, "Event not found.", ["Event not found."], FailureCodes.NotFound);
            }

            EventStatusEnum attemptStatus = (EventStatusEnum)attemptEvent.EventStatusId;
            if (attemptStatus == EventStatusEnum.Published)
            {
                if (mutationAttempted)
                {
                    tenantIdToInvalidate = attemptEvent.TenantId;
                }
                return Success(attemptEvent.Id, mutationAttempted
                    ? "Event published successfully."
                    : "Event is already published.");
            }

            if (attemptEvent.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
            {
                return ConcurrencyFailure(eventId);
            }

            if (!EventLifecycleRules.CanTransition(attemptStatus, EventStatusEnum.Published))
            {
                return ReadinessFailure(eventId, [PublishTransitionReadinessError(attemptStatus)]);
            }

            Guid currentUserId = userContext.GetRequiredUserId();
            var capability = await visitorCapabilities.ResolveAsync(attemptEvent.TenantId, token);
            if (attemptEvent.ParticipationConfiguration?.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired
                && !capability.AllowsAccountRequiredParticipation)
            {
                return BaseCommandResponse.Failure<Guid>("event_visitor_account_onboarding_required",
                    "Account-required participation requires an allowed public onboarding provider.", id: eventId);
            }

            EventLifecyclePolicy policy = await policyProvider.GetEffectivePolicyAsync(
                attemptEvent.TenantId,
                ValidationProfile.EventPublish,
                token);
            if (mode == EventPublicationMode.Ordinary && policy.RequiresApproval)
            {
                return Failure(
                    attemptEvent.Id,
                    "Event cannot be published before approval.",
                    ["This event requires approval before publication."],
                    ApprovalRequiredCode);
            }

            LifecycleReadinessResult readiness = readinessEvaluator.Evaluate(attemptEvent, policy.Profile, policy);
            IReadOnlyList<EventLocation> eventLocations = await eventLocationRepository.GetByEventIdAsync(
                attemptEvent.Id,
                token);
            readiness = EventLocationPublicationReadinessEvaluator.Include(readiness, eventLocations);
            if (!readiness.IsReady)
            {
                return ReadinessFailure(eventId, readiness.Errors.Select(error => error.Message));
            }

            attemptEvent.Publish(occurredAt);
            mutationAttempted = true;

            await eventRepository.Update(attemptEvent);

            await atprotoPublicationPlanner.PlanEventAsync(
                new AtprotoEventPublicationInput(
                    attemptEvent.TenantId,
                    currentUserId,
                    attemptEvent.Id,
                    attemptEvent.ConcurrencyStamp,
                    PdsSyncOperation.Create,
                    federationOutboxId,
                    federationCreatedAt),
                token);

            var publishedAt = new DateTimeOffset(occurredAt, TimeSpan.Zero);
            await outboxRepository.Create(EventPublishedOutboxMessageFactory.CreateNotificationFanoutOutboxMessage(
                notificationFanoutOutboxId,
                attemptEvent,
                publishedAt));

            tenantIdToInvalidate = attemptEvent.TenantId;
            return Success(attemptEvent.Id, "Event published successfully.");
        }, outerToken), cancellationToken);

        if (response.IsSuccess && tenantIdToInvalidate.HasValue)
        {
            await cache.RemoveAsync($"event:detail:{eventId}", cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantIdToInvalidate.Value), cancellationToken);
        }

        return response;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> ConcurrencyFailure(Guid eventId) => Failure(
        eventId,
        "Event was changed by another request.",
        ["Refresh the event and try publishing again."],
        ConcurrencyConflictCode);

    private static BaseCommandResponse<Guid> ReadinessFailure(Guid eventId, IEnumerable<string> errors) => Failure(
        eventId,
        "Event is not ready to publish.",
        errors,
        ReadinessFailedCode);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) => failureCode switch
        {
            null => BaseCommandResponse.Validation(errors, message, id),
            FailureCodes.NotFound => BaseCommandResponse.NotFound(message, id),
            _ => BaseCommandResponse.Failure(failureCode, message, errors, id)
        };

    private static string PublishTransitionReadinessError(EventStatusEnum status) => status switch
    {
        EventStatusEnum.Cancelled => "Event is cancelled and cannot be published.",
        EventStatusEnum.Moderated => "Event is moderated and cannot be published.",
        EventStatusEnum.Archived => "Event is archived and cannot be published.",
        _ => "Event cannot be published from its current status."
    };
}
