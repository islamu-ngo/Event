using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public sealed class ScheduleEventSessionCommandHandler(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IEventDayRepository eventDayRepository,
    IEventScheduleProjectionCalculator scheduleProjectionCalculator,
    IEventLifecyclePolicyProvider policyProvider,
    IEventLifecycleReadinessEvaluator readinessEvaluator,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
    IEventLifecycleScheduler eventLifecycleScheduler,
    TimeProvider timeProvider) : IRequestHandler<ScheduleEventSessionCommand, BaseCommandResponse<Guid>>
{
    private const string ConcurrencyConflictCode = "event_session_schedule_concurrency_conflict";
    private const string ReadinessFailedCode = "event_session_schedule_readiness_failed";
    private const string InvalidStatusFailureCode = "event_session_schedule_invalid_status";

    public async Task<BaseCommandResponse<Guid>> Handle(ScheduleEventSessionCommand command, CancellationToken cancellationToken)
    {
        var validator = new ScheduleEventSessionRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(command.Id, "Event session schedule request is invalid.", validationResult.Errors.Select(error => error.ErrorMessage));
        }

        EventSession? initialSession = await eventSessionRepository.GetById(command.Id);
        if (initialSession is null)
        {
            return Failure(command.Id, "Event session was not found.", ["Event session was not found."]);
        }

        if (!EventSessionLifecycleRules.CanSchedule((EventSessionStatusEnum)initialSession.EventSessionStatusId))
        {
            return Failure(
                command.Id,
                "Event session cannot be scheduled from its current lifecycle state.",
                ["Event session cannot be scheduled from its current lifecycle state."],
                InvalidStatusFailureCode);
        }

        bool initialScheduleIsAlreadyApplied = initialSession.StartTime == command.Request.StartTime.ToUniversalTime()
            && initialSession.EndTime == command.Request.EndTime.ToUniversalTime();
        if (initialScheduleIsAlreadyApplied)
        {
            return Success(initialSession.Id, "Event session schedule is unchanged.");
        }

        if (initialSession.ConcurrencyStamp != command.Request.ExpectedConcurrencyStamp)
        {
            return Failure(
                command.Id,
                "Event session was modified by another request.",
                ["Refresh the event session and try scheduling again."],
                ConcurrencyConflictCode);
        }

        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        Guid occurrenceId = Guid.CreateVersion7();
        Guid pointerOutboxMessageId = Guid.CreateVersion7();
        Guid? eventIdToInvalidate = null;
        Guid? tenantIdToInvalidate = null;
        (BaseCommandResponse<Guid> Response, Guid? ParentEventId, Guid? TenantId) commandResult;

        try
        {
            commandResult = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                EventSession? session = await eventSessionRepository.GetById(command.Id);
                if (session is null)
                {
                    return (Failure(command.Id, "Event session was not found.", ["Event session was not found."]), (Guid?)null, (Guid?)null);
                }

                bool scheduleIsAlreadyApplied = session.StartTime == command.Request.StartTime.ToUniversalTime()
                    && session.EndTime == command.Request.EndTime.ToUniversalTime();
                bool priorAttemptMutated = eventIdToInvalidate.HasValue;
                if (session.ConcurrencyStamp != command.Request.ExpectedConcurrencyStamp
                    && !(priorAttemptMutated && scheduleIsAlreadyApplied))
                {
                    return (Failure(command.Id, "Event session was modified by another request.", ["Refresh the event session and try scheduling again."], ConcurrencyConflictCode), (Guid?)null, (Guid?)null);
                }

                Event? parentEvent = await eventRepository.GetById(session.EventId);
                if (parentEvent is null || parentEvent.TenantId != session.TenantId)
                {
                    return (Failure(command.Id, "Parent event was not found in the current tenant.", ["Parent event was not found in the current tenant."]), (Guid?)null, (Guid?)null);
                }

                DateTimeOffset? previousStartTime = session.StartTime;
                DateTimeOffset? previousEndTime = session.EndTime;
                string previousTitle = session.Title;
                int previousStatusId = session.EventSessionStatusId;
                string timezone = parentEvent.GetEffectiveScheduleTimeZoneId();
                if (!EventSessionLifecycleRules.CanSchedule((EventSessionStatusEnum)previousStatusId))
                {
                    return (Failure(
                        command.Id,
                        "Event session cannot be scheduled from its current lifecycle state.",
                        ["Event session cannot be scheduled from its current lifecycle state."],
                        InvalidStatusFailureCode), (Guid?)null, (Guid?)null);
                }

                if (scheduleIsAlreadyApplied)
                {
                    return (Success(session.Id, "Event session schedule is unchanged."), (Guid?)null, (Guid?)null);
                }

                try
                {
                    session.Reschedule(
                        UtcInstantRange.Create(command.Request.StartTime, command.Request.EndTime),
                        timezone,
                        scheduleProjectionCalculator);
                }
                catch (InvalidOperationException)
                {
                    return (Failure(
                        command.Id,
                        "Event session cannot be scheduled from its current lifecycle state.",
                        ["Event session cannot be scheduled from its current lifecycle state."],
                        InvalidStatusFailureCode), (Guid?)null, (Guid?)null);
                }
                catch (ArgumentException)
                {
                    return (Failure(
                        command.Id,
                        "Event session cannot be scheduled from its current lifecycle state.",
                        ["Event session cannot be scheduled from its current lifecycle state."],
                        InvalidStatusFailureCode), (Guid?)null, (Guid?)null);
                }

                EventDay? matchingDay = session.LocalStartDate is not null
                    ? await eventDayRepository.FindByEventAndLocalDateAsync(
                        parentEvent.Id,
                        session.LocalStartDate.Value,
                        token)
                    : null;
                session.EventDayId = matchingDay?.Id;

                EventLifecyclePolicy policy = await policyProvider.GetEffectivePolicyAsync(
                    session.TenantId,
                    ValidationProfile.SessionSchedule,
                    token);
                LifecycleReadinessResult readiness = readinessEvaluator.Evaluate(
                    session,
                    parentEvent,
                    ValidationProfile.SessionSchedule,
                    policy);
                if (!readiness.IsReady)
                {
                    return (Failure(
                        command.Id,
                        "Event session is not ready to schedule.",
                        readiness.Errors.Select(error => error.Message),
                        ReadinessFailedCode), (Guid?)null, (Guid?)null);
                }

                await eventSessionRepository.UpdateWithRoomOverlapGuardAsync(session, token);
                await RefreshParentScheduleSummaryAsync(parentEvent.Id, token);

                NotificationFanoutChangeField[] changedFields = GetChangedScheduleFields(
                    previousStartTime,
                    previousEndTime,
                    session);
                if (previousStatusId == (int)EventSessionStatusEnum.Published
                    && changedFields.Length > 0)
                {
                    var before = new NotificationFanoutSnapshotV1(
                        parentEvent.Title,
                        previousTitle,
                        previousStartTime,
                        previousEndTime,
                        timezone,
                        Location: null);
                    var after = new NotificationFanoutSnapshotV1(
                        parentEvent.Title,
                        session.Title,
                        session.StartTime,
                        session.EndTime,
                        timezone,
                        Location: null);
                    await fanoutCoordinator.CoordinateInCurrentTransactionAsync(
                        new NotificationFanoutOccurrenceCandidate(
                            occurrenceId,
                            pointerOutboxMessageId,
                            session.TenantId,
                            parentEvent.Id,
                            session.Id,
                            occurredAt,
                            occurredAt,
                            command.Request.ExpectedConcurrencyStamp,
                            NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1(changedFields)),
                            NotificationFanoutTemplateJson.Serialize(before),
                            NotificationFanoutTemplateJson.Serialize(after),
                            NotificationFanoutRecipientTemplateFactory.SessionUpdatedTemplateKey,
                            NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
                            NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                            occurredAt,
                            "event_session_schedule_command",
                            session.Id),
                        token);
                    if (previousStartTime != session.StartTime)
                    {
                        await eventLifecycleScheduler.ReprojectEventRemindersInCurrentTransactionAsync(
                            new EventReminderReprojectionInput(
                                session.TenantId,
                                parentEvent.Id,
                                RegistrationOrderId: null,
                                session.Id,
                                parentEvent.Title,
                                occurredAt,
                                parentEvent.GetEffectiveScheduleTimeZoneId()),
                            token);
                    }
                }

                eventIdToInvalidate ??= parentEvent.Id;
                tenantIdToInvalidate ??= parentEvent.TenantId;
                return (
                    Success(session.Id, "Event session scheduled successfully."),
                    parentEvent.Id,
                    parentEvent.TenantId);
            }, cancellationToken);
        }
        catch (RoomScheduleConflictException ex)
        {
            return Failure(command.Id, "Event session schedule request conflicts with an existing room booking.", [ex.Message], "room_schedule_conflict");
        }
        catch (ConcurrencyConflictException)
        {
            return Failure(
                command.Id,
                "Event session was modified by another request.",
                ["Refresh the event session and try scheduling again."],
                ConcurrencyConflictCode);
        }

        BaseCommandResponse<Guid> response = commandResult.Response;
        if (!response.IsSuccess)
        {
            return response;
        }

        if (eventIdToInvalidate is { } parentEventId && tenantIdToInvalidate is { } tenantId)
        {
            await cache.RemoveAsync($"event:detail:{parentEventId}", cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
        }

        return response;
    }

    private static NotificationFanoutChangeField[] GetChangedScheduleFields(
        DateTimeOffset? previousStartTime,
        DateTimeOffset? previousEndTime,
        EventSession session)
    {
        var fields = new List<NotificationFanoutChangeField>(2);
        if (previousStartTime != session.StartTime)
        {
            fields.Add(NotificationFanoutChangeField.StartTime);
        }

        if (previousEndTime != session.EndTime)
        {
            fields.Add(NotificationFanoutChangeField.EndTime);
        }

        return fields.ToArray();
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
            return;
        }

        scheduleGraph.RecalculateScheduleSummaryFromSessions();
        await eventRepository.Update(scheduleGraph);
    }
}
