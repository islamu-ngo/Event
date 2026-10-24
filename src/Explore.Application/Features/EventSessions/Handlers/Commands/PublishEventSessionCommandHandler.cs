using Explore.Application.Caching;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public sealed class PublishEventSessionCommandHandler(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IEventLifecyclePolicyProvider policyProvider,
    IEventLifecycleReadinessEvaluator readinessEvaluator,
    IEventPublicationMutationBoundary publicationBoundary,
    HybridCache cache,
    TimeProvider timeProvider) : ICommandHandler<PublishEventSessionCommand, BaseCommandResponse<Guid>>
{
    private const string ConcurrencyConflictCode = "event_session_publish_concurrency_conflict";
    private const string ReadinessFailedCode = "event_session_publish_readiness_failed";

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(PublishEventSessionCommand command, CancellationToken cancellationToken)
    {
        var validator = new PublishEventSessionRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(command.Id, "Event session publish request is invalid.", validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var session = await eventSessionRepository.GetById(command.Id);
        if (session is null)
        {
            return Failure(command.Id, "Event session was not found.", ["Event session was not found."]);
        }

        Guid originalEventId = session.EventId;
        Guid originalTenantId = session.TenantId;
        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        bool mutationAttempted = false;
        Guid? cacheParentEventId = null;
        Guid? cacheTenantId = null;
        BaseCommandResponse<Guid> result;
        try
        {
            result = await publicationBoundary.ExecuteAsync(originalEventId, async token =>
            {
                cacheParentEventId = null;
                cacheTenantId = null;
                EventSession? currentSession = await eventSessionRepository.GetByIdForEventForUpdateAsync(
                    command.Id,
                    originalEventId,
                    originalTenantId,
                    token);
                if (currentSession is null)
                {
                    return Failure(command.Id, "Event session was not found.", ["Event session was not found."]);
                }

                Event? currentParentEvent = await eventRepository.GetById(originalEventId);
                if (currentParentEvent is null || currentParentEvent.TenantId != originalTenantId)
                {
                    return Failure(command.Id, "Parent event was not found in the current tenant.", ["Parent event was not found in the current tenant."]);
                }

                bool alreadyPublished = currentSession.EventSessionStatusId == (int)EventSessionStatusEnum.Published;
                if (alreadyPublished)
                {
                    if (mutationAttempted)
                    {
                        cacheParentEventId = originalEventId;
                        cacheTenantId = originalTenantId;
                    }
                    return Success(currentSession.Id, "Event session is already published.");
                }

                if (currentSession.ConcurrencyStamp != command.Request.ExpectedConcurrencyStamp)
                {
                    return Failure(
                        command.Id,
                        "Event session was modified by another request.",
                        ["Refresh the event session and try publishing again."],
                        ConcurrencyConflictCode);
                }

                EventLifecyclePolicy currentPolicy = await policyProvider.GetEffectivePolicyAsync(
                    currentSession.TenantId,
                    ValidationProfile.SessionPublish,
                    token);
                LifecycleReadinessResult currentReadiness = readinessEvaluator.Evaluate(
                    currentSession,
                    currentParentEvent,
                    ValidationProfile.SessionPublish,
                    currentPolicy);
                if (!currentReadiness.IsReady)
                {
                    return Failure(
                        command.Id,
                        "Event session is not ready to publish.",
                        currentReadiness.Errors.Select(error => error.Message),
                        ReadinessFailedCode);
                }

                currentSession.Publish((EventStatusEnum)currentParentEvent.EventStatusId, occurredAt);
                mutationAttempted = true;
                await eventSessionRepository.Update(currentSession);
                await RefreshParentScheduleSummaryAsync(currentParentEvent.Id, token);

                cacheParentEventId = originalEventId;
                cacheTenantId = originalTenantId;
                return Success(currentSession.Id, "Event session published successfully.");
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Failure(
                command.Id,
                "Event session was modified by another request.",
                ["Refresh the event session and try publishing again."],
                ConcurrencyConflictCode);
        }

        if (result.FailureCode is "event_publish_restricted" or "event_publish_authority_unavailable")
        {
            return BaseCommandResponse.Failure(
                result.FailureCode == "event_publish_restricted"
                    ? "event_session_publish_restricted" : "event_session_publish_authority_unavailable",
                result.Message, result.Errors, command.Id);
        }

        if (result.IsSuccess && cacheParentEventId is { } parentEventId && cacheTenantId is { } tenantId)
        {
            await cache.RemoveByTagAsync(CacheTags.Event(parentEventId), cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
        }
        return result;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);

    private async Task RefreshParentScheduleSummaryAsync(Guid eventId, CancellationToken cancellationToken)
    {
        Event? scheduleGraph = await eventRepository.GetScheduleGraphForUpdateAsync(eventId, cancellationToken);
        if (scheduleGraph is null)
        {
            throw new InvalidOperationException("Parent event schedule graph was not found during session publication.");
        }

        scheduleGraph.RecalculateScheduleSummaryFromSessions();
        await eventRepository.Update(scheduleGraph);
    }
}
