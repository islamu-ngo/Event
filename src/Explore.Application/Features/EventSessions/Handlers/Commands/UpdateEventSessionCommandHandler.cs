using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public class UpdateEventSessionCommandHandler : IRequestHandler<UpdateEventSessionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IRegistrationModeRepository _registrationModeRepository;
    private readonly IEventSessionKindRepository _eventSessionKindRepository;
    private readonly IEventSessionIslamicAspectRepository _eventSessionIslamicAspectRepository;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly HybridCache _cache;
    private readonly NotificationFanoutOccurrenceCoordinator _fanoutCoordinator;
    private readonly IEventLifecycleScheduler _eventLifecycleScheduler;
    private readonly IRefundCampaignRepository _refundCampaignRepository;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateEventSessionCommandHandler(
        IEventSessionRepository eventSessionRepository,
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        ILocationRoomRepository locationRoomRepository,
        IRegistrationModeRepository registrationModeRepository,
        IEventSessionKindRepository eventSessionKindRepository,
        IEventSessionIslamicAspectRepository eventSessionIslamicAspectRepository,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        IEventDayRepository eventDayRepository,
        IStorageObjectRepository storageObjectRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        HybridCache cache,
        NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
        IEventLifecycleScheduler eventLifecycleScheduler,
        IRefundCampaignRepository refundCampaignRepository,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _eventSessionRepository = eventSessionRepository;
        _eventRepository = eventRepository;
        _locationRepository = locationRepository;
        _locationRoomRepository = locationRoomRepository;
        _registrationModeRepository = registrationModeRepository;
        _eventSessionKindRepository = eventSessionKindRepository;
        _eventSessionIslamicAspectRepository = eventSessionIslamicAspectRepository;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _eventDayRepository = eventDayRepository;
        _storageObjectRepository = storageObjectRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _cache = cache;
        _fanoutCoordinator = fanoutCoordinator;
        _eventLifecycleScheduler = eventLifecycleScheduler;
        _refundCampaignRepository = refundCampaignRepository;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventSessionCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventSessionDtoValidator(
            _eventRepository,
            _locationRepository,
            _locationRoomRepository,
            _registrationModeRepository,
            _eventSessionKindRepository);
        var validationResult = await validator.ValidateAsync(request.EventSessionDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session update failed.");
        }

        DateTime occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
        Guid occurrenceId = Guid.CreateVersion7();
        Guid pointerOutboxMessageId = Guid.CreateVersion7();
        BaseCommandResponse<Guid>? transactionFailure = null;
        Guid updatedSessionId = Guid.Empty;
        Guid previousEventIdForCache = Guid.Empty;
        Guid parentEventIdForCache = Guid.Empty;
        Guid tenantIdForCache = Guid.Empty;
        bool eventChangedForCache = false;

        try
        {
            bool updated = await _unitOfWork.ExecuteSerializableAsync(async token =>
            {
                transactionFailure = null;
                EventSession? eventSession = await _eventSessionRepository.GetById(request.EventSessionId);
                if (eventSession is null)
                {
                    transactionFailure = CreateFailureResponse("Event session not found.", failureCode: FailureCodes.NotFound);
                    return false;
                }

                if (eventSession.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
                {
                    throw new ConcurrencyConflictException(
                        ConcurrencyConflictException.ConcurrentUpdate,
                        "The event session was modified by another request. Reload and retry.",
                        nameof(EventSession),
                        eventSession.Id.ToString());
                }

                Guid previousEventId = eventSession.EventId;
                Guid parentEventId = request.EventSessionDto.Event?.EventId ?? previousEventId;
                Event? parentEvent = await _eventRepository.GetById(parentEventId);
                if (parentEvent is null || parentEvent.TenantId != eventSession.TenantId)
                {
                    transactionFailure = CreateFailureResponse("Event does not belong to the same tenant as the session.");
                    return false;
                }

                Guid? featuredImageId = request.EventSessionDto.FeaturedImage?.Value.HasValue == true
                    ? request.EventSessionDto.FeaturedImage.Value.Value
                    : null;
                if (!await ImageReferenceEligibility.AreEligibleAsync(
                        _storageObjectRepository,
                        eventSession.TenantId,
                        featuredImageId))
                {
                    transactionFailure = CreateFailureResponse(
                        "Featured image must be an active public safe-raster object in the current tenant.");
                    return false;
                }

                bool eventChanged = previousEventId != parentEvent.Id;
                if (eventChanged && eventSession.EventSessionStatusId == (int)EventSessionStatusEnum.Published)
                {
                    transactionFailure = CreateFailureResponse(
                        "Event session update failed.",
                        "Published event sessions cannot be moved to another event until attendee transfer notifications are supported.",
                        "event_session_update_invalid_status");
                    return false;
                }

                if (!HasValidFinalIslamicSchedulingState(eventSession, request.EventSessionDto))
                {
                    transactionFailure = CreateFailureResponse(
                        "Event session update failed.",
                        EventSessionIslamicAspectValidationRules.SchedulingStateMessage);
                    return false;
                }

                var placement = await ResolveFinalPlacementAsync(
                    eventSession,
                    request.EventSessionDto,
                    token);
                if (!placement.Success)
                {
                    transactionFailure = CreateFailureResponse("Event session update failed.", placement.Message);
                    return false;
                }

                Guid? previousEventLocationId = eventSession.EventLocationId;
                Guid? previousLocationId = eventSession.LocationId;
                Guid? previousRoomId = eventSession.RoomId;
                DateTimeOffset? previousStartTime = eventSession.StartTime;
                DateTimeOffset? previousEndTime = eventSession.EndTime;
                string previousSessionTitle = eventSession.Title;
                int previousStatusId = eventSession.EventSessionStatusId;
                string timezone = parentEvent.GetEffectiveScheduleTimeZoneId();
                EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                    parentEvent.Id,
                    placement.LocationId,
                    previousEventLocationId,
                    token);
                if (eventChanged)
                {
                    await _eventSessionRepository.MoveToEventAsync(
                        eventSession,
                        parentEvent.Id,
                        eventLocation,
                        placement.RoomId,
                        token);
                }
                else
                {
                    eventSession.AssignEventLocation(eventLocation);
                    eventSession.RoomId = placement.RoomId;
                }

                ApplyFeaturedImage(eventSession, request.EventSessionDto.FeaturedImage);
                ApplySortOrder(eventSession, request.EventSessionDto.SortOrder);
                ApplyTitle(eventSession, request.EventSessionDto.Title);
                ApplyKind(eventSession, request.EventSessionDto.Kind);
                ApplyDescription(eventSession, request.EventSessionDto.Description);
                ApplySlug(eventSession, request.EventSessionDto.Slug);
                ApplyMaxAudienceAttendees(eventSession, request.EventSessionDto.MaxAudienceAttendees);
                ApplyRegistrationMode(eventSession, request.EventSessionDto.RegistrationMode);
                await ApplyScheduleAsync(eventSession, parentEvent, eventChanged, request.EventSessionDto.Schedule, token);
                await _eventSessionRepository.UpdateWithRoomOverlapGuardAsync(eventSession, token);
                await ApplyIslamicAspectAsync(eventSession.Id, request.EventSessionDto.IslamicAspect, eventSession.EndTimeType, token);
                NotificationFanoutChangeField[] changedFields = GetMaterialFanoutChanges(
                    previousStartTime,
                    previousEndTime,
                    previousLocationId,
                    previousRoomId,
                    eventSession);
                if (previousStatusId == (int)EventSessionStatusEnum.Published
                    && changedFields.Length > 0)
                {
                    bool locationChanged = changedFields.Any(field =>
                        field is NotificationFanoutChangeField.Location or NotificationFanoutChangeField.Room);
                    NotificationFanoutLocationSnapshotV1? beforeLocation = locationChanged
                        ? await CreateLocationSnapshotAsync(
                            eventSession.TenantId,
                            previousEventLocationId,
                            previousLocationId,
                            previousRoomId)
                        : null;
                    NotificationFanoutLocationSnapshotV1? afterLocation = locationChanged
                        ? await CreateLocationSnapshotAsync(
                            eventSession.TenantId,
                            eventSession.EventLocationId,
                            eventSession.LocationId,
                            eventSession.RoomId)
                        : null;
                    var before = new NotificationFanoutSnapshotV1(
                        parentEvent.Title,
                        previousSessionTitle,
                        previousStartTime,
                        previousEndTime,
                        timezone,
                        beforeLocation);
                    var after = new NotificationFanoutSnapshotV1(
                        parentEvent.Title,
                        eventSession.Title,
                        eventSession.StartTime,
                        eventSession.EndTime,
                        timezone,
                        afterLocation);
                    await _fanoutCoordinator.CoordinateInCurrentTransactionAsync(
                        new NotificationFanoutOccurrenceCandidate(
                            occurrenceId,
                            pointerOutboxMessageId,
                            eventSession.TenantId,
                            parentEvent.Id,
                            eventSession.Id,
                            occurredAt,
                            occurredAt,
                            request.ExpectedConcurrencyStamp,
                            NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1(changedFields)),
                            NotificationFanoutTemplateJson.Serialize(before),
                            NotificationFanoutTemplateJson.Serialize(after),
                            NotificationFanoutRecipientTemplateFactory.SessionUpdatedTemplateKey,
                            NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
                            NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                            occurredAt,
                            "event_session_update_command",
                            eventSession.Id),
                        token);
                    RefundCampaign materialChange = RefundCampaign.CreateMaterialChange(
                        Guid.CreateVersion7(), eventSession.TenantId, parentEvent.Id,
                        _userContext.GetRequiredUserId(), "Published event session terms changed.", occurredAt);
                    await _refundCampaignRepository.CreateAsync(
                        materialChange,
                        RefundOutboxMessageFactory.CreateCampaignProcess(materialChange, occurredAt),
                        token);
                    if (previousStartTime != eventSession.StartTime)
                    {
                        await _eventLifecycleScheduler.ReprojectEventRemindersInCurrentTransactionAsync(
                            new EventReminderReprojectionInput(
                                eventSession.TenantId,
                                parentEvent.Id,
                                RegistrationOrderId: null,
                                eventSession.Id,
                                parentEvent.Title,
                                occurredAt,
                                parentEvent.GetEffectiveScheduleTimeZoneId()),
                            token);
                    }
                }

                await _eventLocationAttachmentService.DetachIfUnreferencedAsync(previousEventLocationId, token);
                updatedSessionId = eventSession.Id;
                previousEventIdForCache = previousEventId;
                parentEventIdForCache = parentEvent.Id;
                tenantIdForCache = parentEvent.TenantId;
                eventChangedForCache = eventChanged;
                return true;
            }, cancellationToken);

            if (!updated)
            {
                return transactionFailure
                    ?? throw new InvalidOperationException("Event session update ended without a failure response.");
            }
        }
        catch (RoomScheduleConflictException ex)
        {
            return BaseCommandResponse.Failure<Guid>(
                "room_schedule_conflict",
                "Event session update failed.",
                [ex.Message]);
        }

        if (eventChangedForCache)
        {
            await _cache.RemoveAsync($"event:detail:{previousEventIdForCache}", cancellationToken);
        }

        await _cache.RemoveAsync($"event:detail:{parentEventIdForCache}", cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantIdForCache), cancellationToken);

        return BaseCommandResponse.Success(updatedSessionId, "Event session updated successfully.");
    }

    private static BaseCommandResponse<Guid> CreateFailureResponse(
        string message,
        string? error = null,
        string? failureCode = null) => failureCode switch
        {
            FailureCodes.NotFound => BaseCommandResponse.NotFound<Guid>(message),
            null => BaseCommandResponse.Validation<Guid>([error ?? message], message),
            _ => BaseCommandResponse.Failure<Guid>(failureCode, message, error is null ? null : [error])
        };

    private static NotificationFanoutChangeField[] GetMaterialFanoutChanges(
        DateTimeOffset? previousStartTime,
        DateTimeOffset? previousEndTime,
        Guid? previousLocationId,
        Guid? previousRoomId,
        EventSession eventSession)
    {
        var changedFields = new List<NotificationFanoutChangeField>(4);
        if (previousStartTime != eventSession.StartTime)
        {
            changedFields.Add(NotificationFanoutChangeField.StartTime);
        }

        if (previousEndTime != eventSession.EndTime)
        {
            changedFields.Add(NotificationFanoutChangeField.EndTime);
        }

        if (previousLocationId != eventSession.LocationId)
        {
            changedFields.Add(NotificationFanoutChangeField.Location);
        }

        if (previousRoomId != eventSession.RoomId)
        {
            changedFields.Add(NotificationFanoutChangeField.Room);
        }

        return changedFields.ToArray();
    }

    private async Task<NotificationFanoutLocationSnapshotV1?> CreateLocationSnapshotAsync(
        Guid tenantId,
        Guid? eventLocationId,
        Guid? locationId,
        Guid? roomId)
    {
        if (!eventLocationId.HasValue)
        {
            return null;
        }

        Location? location = locationId.HasValue
            ? await _locationRepository.GetById(locationId.Value)
            : null;
        if (locationId.HasValue
            && (location is null || location.TenantId != tenantId))
        {
            throw new InvalidOperationException("Fanout location snapshot crossed its tenant boundary.");
        }

        LocationRoom? room = roomId.HasValue
            ? await _locationRoomRepository.GetById(roomId.Value)
            : null;
        if (roomId.HasValue
            && (room is null
                || room.TenantId != tenantId
                || !locationId.HasValue
                || room.LocationId != locationId.Value))
        {
            throw new InvalidOperationException("Fanout room snapshot does not belong to its tenant and location.");
        }

        return new NotificationFanoutLocationSnapshotV1(
            eventLocationId.Value,
            roomId,
            location?.Country,
            location?.City,
            location?.FullName,
            room?.Name,
            location?.Address,
            location?.Postcode);
    }

    private async Task ApplyScheduleAsync(
        EventSession eventSession,
        Event parentEvent,
        bool eventChanged,
        UpdateEventSessionScheduleDto? group,
        CancellationToken cancellationToken)
    {
        if (group is null)
        {
            if (!eventChanged)
            {
                return;
            }

            eventSession.ReprojectLocalTimes(parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty, _scheduleProjectionCalculator);
            await RelinkEventDayAsync(eventSession, parentEvent.Id, cancellationToken);
            return;
        }

        if (group.EndTimeType.HasValue)
        {
            eventSession.EndTimeType = group.EndTimeType.Value;
        }

        DateTimeOffset? startTime = group.StartTime.Value;
        DateTimeOffset? endTime = group.EndTime.Value;
        string timezone = parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty;

        if (startTime is null)
        {
            eventSession.Unschedule();
            eventSession.EventDayId = null;
            return;
        }

        switch (eventSession.EndTimeType)
        {
            case SessionEndTimeType.Fixed when endTime is { } fixedEnd:
                eventSession.Reschedule(
                    UtcInstantRange.Create(startTime.Value, fixedEnd),
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            case SessionEndTimeType.OpenEnded:
                eventSession.ScheduleOpenEnded(
                    startTime.Value,
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            case SessionEndTimeType.RelativeToPrayer when endTime is { } relativeEnd:
                eventSession.ScheduleRelativeToPrayer(
                    UtcInstantRange.Create(startTime.Value, relativeEnd),
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            case SessionEndTimeType.RelativeToPrayer:
                eventSession.ScheduleRelativeToPrayer(
                    startTime.Value,
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            default:
                throw new InvalidOperationException("Event session schedule shape is not supported.");
        }
        await RelinkEventDayAsync(eventSession, parentEvent.Id, cancellationToken);
    }

    private async Task RelinkEventDayAsync(EventSession eventSession, Guid eventId, CancellationToken cancellationToken)
    {
        var matchingDay = eventSession.LocalStartDate is not null
            ? await _eventDayRepository.FindByEventAndLocalDateAsync(
                eventId,
                eventSession.LocalStartDate.Value,
                cancellationToken)
            : null;
        eventSession.EventDayId = matchingDay?.Id;
    }

    private async Task ApplyIslamicAspectAsync(
        Guid eventSessionId,
        UpdateEventSessionIslamicAspectUpdateDto? group,
        SessionEndTimeType endTimeType,
        CancellationToken cancellationToken)
    {
        if (group is null)
        {
            return;
        }

        var existingIslamicAspect = await _eventSessionIslamicAspectRepository.GetById(eventSessionId);
        if (group.Value.Value is null)
        {
            if (existingIslamicAspect != null)
            {
                await _eventSessionIslamicAspectRepository.Delete(existingIslamicAspect);
            }

            return;
        }

        if (existingIslamicAspect == null)
        {
            var newAspect = new EventSessionIslamicAspect
            {
                EventSessionId = eventSessionId,
                EventSession = null
            };
            ApplyIslamicAspectDto(newAspect, group.Value.Value, endTimeType);
            await _eventSessionIslamicAspectRepository.Create(newAspect);
            return;
        }

        ApplyIslamicAspectDto(existingIslamicAspect, group.Value.Value, endTimeType);
        await _eventSessionIslamicAspectRepository.Update(existingIslamicAspect);
    }

    private static bool HasValidFinalIslamicSchedulingState(EventSession eventSession, UpdateEventSessionDto dto)
    {
        if (dto.IslamicAspect?.Value.HasValue != true || dto.IslamicAspect.Value.Value is null)
        {
            return true;
        }

        var locationId = dto.Location?.Value.HasValue == true ? dto.Location.Value.Value : eventSession.LocationId;
        return EventSessionIslamicAspectValidationRules.HasValidSchedulingState(dto.IslamicAspect.Value.Value, locationId);
    }

    private async Task<(bool Success, string Message, Guid? LocationId, Guid? RoomId)> ResolveFinalPlacementAsync(
        EventSession eventSession,
        UpdateEventSessionDto dto,
        CancellationToken cancellationToken)
    {
        Guid? locationId = dto.Location?.Value.HasValue == true
            ? dto.Location.Value.Value
            : eventSession.LocationId;
        Guid? roomId = dto.Room?.Value.HasValue == true
            ? dto.Room.Value.Value
            : eventSession.RoomId;

        if (!roomId.HasValue)
        {
            return (true, string.Empty, locationId, null);
        }

        LocationRoom? room = await _locationRoomRepository.GetById(roomId.Value);
        if (room is null || room.TenantId != eventSession.TenantId)
        {
            return (false, "Room does not belong to the same tenant as the session.", locationId, roomId);
        }

        if (locationId.HasValue && locationId.Value != room.LocationId)
        {
            return (false, "Room must belong to the selected location.", locationId, roomId);
        }

        return (true, string.Empty, room.LocationId, roomId);
    }

    private static void ApplyFeaturedImage(EventSession eventSession, UpdateEventSessionFeaturedImageDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.FeaturedImageId = group.Value.Value;
        }
    }

    private static void ApplySortOrder(EventSession eventSession, UpdateEventSessionSortOrderDto? group)
    {
        if (group is not null)
        {
            eventSession.SortOrder = group.Value;
        }
    }

    private static void ApplyTitle(EventSession eventSession, UpdateEventSessionTitleDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.Title = group.Value.Value;
        }
    }

    private static void ApplyKind(EventSession eventSession, UpdateEventSessionKindDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.EventSessionKindId = group.Value.Value;
        }
    }

    private static void ApplyDescription(EventSession eventSession, UpdateEventSessionDescriptionDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.Description = group.Value.Value;
        }
    }

    private static void ApplySlug(EventSession eventSession, UpdateEventSessionSlugDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.Slug = group.Value.Value;
        }
    }

    private static void ApplyMaxAudienceAttendees(EventSession eventSession, UpdateEventSessionMaxAudienceAttendeesDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.MaxAudienceAttendees = group.Value.Value;
        }
    }

    private static void ApplyRegistrationMode(EventSession eventSession, UpdateEventSessionRegistrationModeDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            eventSession.RegistrationModeId = group.Value.Value;
        }
    }

    private static void ApplyIslamicAspectDto(
        EventSessionIslamicAspect aspect,
        EventSessionIslamicAspectDto dto,
        SessionEndTimeType endTimeType)
    {
        aspect.ApplyScheduling(dto.StartTimeType, dto.ReferencePrayer, dto.OffsetMinutes);
        aspect.ApplyEndTimeScheduling(endTimeType, dto.EndReferencePrayer, dto.EndOffsetMinutes);
        aspect.RequiresWudu = dto.RequiresWudu;
        aspect.RitualRequirementsJson = dto.RitualRequirementsJson;
    }
}
