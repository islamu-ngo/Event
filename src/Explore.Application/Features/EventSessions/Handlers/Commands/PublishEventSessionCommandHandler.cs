using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public sealed class PublishEventSessionCommandHandler(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IEventLifecyclePolicyProvider policyProvider,
    IEventLifecycleReadinessEvaluator readinessEvaluator,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    TimeProvider timeProvider) : IRequestHandler<PublishEventSessionCommand, BaseCommandResponse<Guid>>
{
    private const string ConcurrencyConflictCode = "event_session_publish_concurrency_conflict";
    private const string ReadinessFailedCode = "event_session_publish_readiness_failed";

    public async Task<BaseCommandResponse<Guid>> Handle(PublishEventSessionCommand command, CancellationToken cancellationToken)
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

        if (session.EventSessionStatusId == (int)EventSessionStatusEnum.Published)
        {
            return Success(session.Id, "Event session is already published.");
        }

        if (session.ConcurrencyStamp != command.Request.ExpectedConcurrencyStamp)
        {
            return Failure(command.Id, "Event session was modified by another request.", ["Refresh the event session and try publishing again."], ConcurrencyConflictCode);
        }

        var parentEvent = await eventRepository.GetById(session.EventId);
        if (parentEvent is null || parentEvent.TenantId != session.TenantId)
        {
            return Failure(command.Id, "Parent event was not found in the current tenant.", ["Parent event was not found in the current tenant."]);
        }

        EventLifecyclePolicy policy = await policyProvider.GetEffectivePolicyAsync(session.TenantId, ValidationProfile.SessionPublish, cancellationToken);
        LifecycleReadinessResult readiness = readinessEvaluator.Evaluate(session, parentEvent, ValidationProfile.SessionPublish, policy);
        if (!readiness.IsReady)
        {
            return Failure(command.Id, "Event session is not ready to publish.", readiness.Errors.Select(error => error.Message), ReadinessFailedCode);
        }

        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        bool mutationAttempted = false;
        (BaseCommandResponse<Guid> Response, Guid? ParentEventId, Guid? TenantId) result;
        try
        {
            result = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                EventSession? currentSession = await eventSessionRepository.GetByIdForEventAsync(
                    command.Id,
                    session.EventId,
                    session.TenantId,
                    token);
                if (currentSession is null)
                {
                    return NoCache(Failure(command.Id, "Event session was not found.", ["Event session was not found."]));
                }

                Event? currentParentEvent = await eventRepository.GetById(currentSession.EventId);
                if (currentParentEvent is null || currentParentEvent.TenantId != currentSession.TenantId)
                {
                    return NoCache(Failure(command.Id, "Parent event was not found in the current tenant.", ["Parent event was not found in the current tenant."]));
                }

                bool alreadyPublished = currentSession.EventSessionStatusId == (int)EventSessionStatusEnum.Published;
                if (mutationAttempted && alreadyPublished)
                {
                    return WithCacheIdentity(Success(currentSession.Id, "Event session published successfully."), currentParentEvent);
                }

                if (currentSession.ConcurrencyStamp != command.Request.ExpectedConcurrencyStamp)
                {
                    return NoCache(Failure(
                        command.Id,
                        "Event session was modified by another request.",
                        ["Refresh the event session and try publishing again."],
                        ConcurrencyConflictCode));
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
                    return NoCache(Failure(
                        command.Id,
                        "Event session is not ready to publish.",
                        currentReadiness.Errors.Select(error => error.Message),
                        ReadinessFailedCode));
                }

                currentSession.Publish((EventStatusEnum)currentParentEvent.EventStatusId, occurredAt);
                mutationAttempted = true;
                await eventSessionRepository.Update(currentSession);
                await RefreshParentScheduleSummaryAsync(currentParentEvent.Id, token);

                return WithCacheIdentity(Success(currentSession.Id, "Event session published successfully."), currentParentEvent);
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

        if (!result.Response.IsSuccess
            || result.ParentEventId is not { } parentEventId
            || result.TenantId is not { } tenantId)
        {
            return result.Response;
        }

        await cache.RemoveAsync($"event:detail:{parentEventId}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
        return result.Response;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);

    private static (BaseCommandResponse<Guid> Response, Guid? ParentEventId, Guid? TenantId) NoCache(
        BaseCommandResponse<Guid> response) => (response, null, null);

    private static (BaseCommandResponse<Guid> Response, Guid? ParentEventId, Guid? TenantId) WithCacheIdentity(
        BaseCommandResponse<Guid> response,
        Event parentEvent) => (response, parentEvent.Id, parentEvent.TenantId);

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
