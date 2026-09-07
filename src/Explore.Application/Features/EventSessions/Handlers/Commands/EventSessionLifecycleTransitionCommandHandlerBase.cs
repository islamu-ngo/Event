using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public abstract class EventSessionLifecycleTransitionCommandHandlerBase<TCommand>(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    TimeProvider timeProvider) : IRequestHandler<TCommand, BaseCommandResponse<Guid>>
    where TCommand : IEventSessionLifecycleTransitionCommand
{
    protected abstract string ActionName { get; }
    protected abstract string PastTenseActionName { get; }
    protected abstract string ConcurrencyFailureCode { get; }
    protected abstract string InvalidStatusFailureCode { get; }

    protected virtual TransitionAttempt CreateTransitionAttempt() =>
        new(timeProvider.GetUtcNow().UtcDateTime, Guid.Empty, Guid.Empty);

    protected virtual Task AfterTransitionInCurrentTransactionAsync(
        EventSession session,
        Event parentEvent,
        int previousStatusId,
        Guid expectedConcurrencyStamp,
        TransitionAttempt attempt,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<BaseCommandResponse<Guid>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        var validator = new EventSessionLifecycleRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.Id,
                $"Event session {ActionName} request is invalid.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var session = await eventSessionRepository.GetById(request.Id);
        if (session is null)
        {
            return Failure(request.Id, "Event session was not found.", ["Event session was not found."]);
        }

        if (IsAlreadyApplied(session))
        {
            return Success(session.Id, $"Event session is already {PastTenseActionName}.");
        }

        if (session.ConcurrencyStamp != request.Request.ExpectedConcurrencyStamp)
        {
            return Failure(
                request.Id,
                "Event session was modified by another request.",
                ["Refresh the event session and try again."],
                ConcurrencyFailureCode);
        }

        var parentEvent = await eventRepository.GetById(session.EventId);
        if (parentEvent is null || parentEvent.TenantId != session.TenantId)
        {
            return Failure(
                request.Id,
                "Parent event was not found in the current tenant.",
                ["Parent event was not found in the current tenant."]);
        }

        TransitionAttempt attempt = CreateTransitionAttempt();
        Guid? eventIdToInvalidate = null;
        Guid? tenantIdToInvalidate = null;
        BaseCommandResponse<Guid> response;
        try
        {
            response = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                EventSession? currentSession = await eventSessionRepository.GetByIdForEventAsync(
                    request.Id,
                    session.EventId,
                    session.TenantId,
                    token);
                if (currentSession is null)
                {
                    return Failure(request.Id, "Event session was not found.", ["Event session was not found."]);
                }

                bool alreadyApplied = IsAlreadyApplied(currentSession);
                bool priorAttemptMutated = eventIdToInvalidate.HasValue;
                if (currentSession.ConcurrencyStamp != request.Request.ExpectedConcurrencyStamp
                    && !(priorAttemptMutated && alreadyApplied))
                {
                    return Failure(
                        request.Id,
                        "Event session was modified by another request.",
                        ["Refresh the event session and try again."],
                        ConcurrencyFailureCode);
                }

                Event? currentParentEvent = await eventRepository.GetById(currentSession.EventId);
                if (currentParentEvent is null || currentParentEvent.TenantId != currentSession.TenantId)
                {
                    return Failure(
                        request.Id,
                        "Parent event was not found in the current tenant.",
                        ["Parent event was not found in the current tenant."]);
                }

                if (alreadyApplied)
                {
                    return Success(currentSession.Id, $"Event session is already {PastTenseActionName}.");
                }

                int previousStatusId = currentSession.EventSessionStatusId;
                try
                {
                    _ = ApplyTransition(currentSession, (EventStatusEnum)currentParentEvent.EventStatusId, attempt.OccurredAt);
                }
                catch (InvalidOperationException)
                {
                    return Failure(
                        currentSession.Id,
                        $"Event session cannot be {PastTenseActionName} from its current lifecycle state.",
                        [$"Event session cannot be {PastTenseActionName} from its current lifecycle state."],
                        InvalidStatusFailureCode);
                }
                catch (ArgumentException)
                {
                    return Failure(
                        currentSession.Id,
                        $"Event session cannot be {PastTenseActionName} from its current lifecycle state.",
                        [$"Event session cannot be {PastTenseActionName} from its current lifecycle state."],
                        InvalidStatusFailureCode);
                }

                await eventSessionRepository.Update(currentSession);
                await RefreshParentScheduleSummaryAsync(currentParentEvent.Id, token);
                await AfterTransitionInCurrentTransactionAsync(
                    currentSession,
                    currentParentEvent,
                    previousStatusId,
                    request.Request.ExpectedConcurrencyStamp,
                    attempt,
                    token);
                eventIdToInvalidate ??= currentParentEvent.Id;
                tenantIdToInvalidate ??= currentParentEvent.TenantId;

                return Success(currentSession.Id, $"Event session {PastTenseActionName} successfully.");
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Failure(
                request.Id,
                "Event session was modified by another request.",
                ["Refresh the event session and try again."],
                ConcurrencyFailureCode);
        }

        if (!response.IsSuccess || !eventIdToInvalidate.HasValue || !tenantIdToInvalidate.HasValue)
        {
            return response;
        }

        await cache.RemoveAsync($"event:detail:{eventIdToInvalidate.Value}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantIdToInvalidate.Value), cancellationToken);
        return response;
    }

    protected abstract bool IsAlreadyApplied(EventSession session);

    protected abstract bool ApplyTransition(EventSession session, EventStatusEnum parentEventStatus, DateTime occurredAt);

    private async Task RefreshParentScheduleSummaryAsync(Guid eventId, CancellationToken cancellationToken)
    {
        Event? scheduleGraph = await eventRepository.GetScheduleGraphForUpdateAsync(eventId, cancellationToken);
        if (scheduleGraph is null)
        {
            return;
        }

        scheduleGraph.RecalculateScheduleSummaryFromSessions();
        await eventRepository.Update(scheduleGraph);
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);

    protected sealed record TransitionAttempt(
        DateTime OccurredAt,
        Guid OccurrenceId,
        Guid PointerOutboxMessageId);
}
