using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSession.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Domain.Services.Scheduling;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public class CreateEventSessionCommandHandler : IRequestHandler<CreateEventSessionCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IRegistrationModeRepository _registrationModeRepository;
    private readonly IEventSessionKindRepository _eventSessionKindRepository;
    private readonly IEventSessionIslamicAspectRepository _eventSessionIslamicAspectRepository;
    private readonly IEventSessionTemplateRepository _eventSessionTemplateRepository;
    private readonly IEventSessionCustomPropertyRepository _eventSessionCustomPropertyRepository;
    private readonly IEventSessionCustomPropertyProjectionUpdater _projectionUpdater;
    private readonly IEventSessionTemplateInstantiationService _instantiationService;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly IMapper _mapper;

    public CreateEventSessionCommandHandler(
        IEventSessionRepository eventSessionRepository,
        IEventRepository eventRepository,
        ILocationRepository locationRepository,
        IRegistrationModeRepository registrationModeRepository,
        IEventSessionKindRepository eventSessionKindRepository,
        IEventSessionIslamicAspectRepository eventSessionIslamicAspectRepository,
        IEventSessionTemplateRepository eventSessionTemplateRepository,
        IEventSessionCustomPropertyRepository eventSessionCustomPropertyRepository,
        IEventSessionCustomPropertyProjectionUpdater projectionUpdater,
        IEventSessionTemplateInstantiationService instantiationService,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        IEventDayRepository eventDayRepository,
        IStorageObjectRepository storageObjectRepository,
        IUnitOfWork unitOfWork,
        EventLocationAttachmentService eventLocationAttachmentService,
        IMapper mapper)
    {
        _eventSessionRepository = eventSessionRepository;
        _eventRepository = eventRepository;
        _locationRepository = locationRepository;
        _registrationModeRepository = registrationModeRepository;
        _eventSessionKindRepository = eventSessionKindRepository;
        _eventSessionIslamicAspectRepository = eventSessionIslamicAspectRepository;
        _eventSessionTemplateRepository = eventSessionTemplateRepository;
        _eventSessionCustomPropertyRepository = eventSessionCustomPropertyRepository;
        _projectionUpdater = projectionUpdater;
        _instantiationService = instantiationService;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _eventDayRepository = eventDayRepository;
        _storageObjectRepository = storageObjectRepository;
        _unitOfWork = unitOfWork;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventSessionCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionDtoValidator(
            _eventRepository,
            _locationRepository,
            _registrationModeRepository,
            _eventSessionKindRepository,
            _eventSessionTemplateRepository,
            _eventSessionRepository);
        var validationResult = await validator.ValidateAsync(request.EventSessionDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event session creation failed.");
        }

        // Fetch the parent event to inherit its TenantId (defense-in-depth: global query filter already scopes by tenant)
        var parentEvent = await _eventRepository.GetById(request.EventSessionDto.EventId);
        if (parentEvent == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Event not found in the current tenant."],
                "Event not found in the current tenant.");
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                parentEvent.TenantId,
                request.EventSessionDto.FeaturedImageId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Featured image must be an active public safe-raster object in the current tenant."],
                "Event session creation failed.");
        }

        var eventSession = _mapper.Map<EventSession>(request.EventSessionDto);
        eventSession.CurrentAudienceAttendees = 0;
        eventSession.TenantId = parentEvent.TenantId;

        // Populate cached local projection fields via the single authorized write path on EventSession.
        // Handlers never touch LocalStart*/LocalEnd* directly; the aggregate method consumes the calculator.
        string timezone = parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty;

        switch (request.EventSessionDto.EndTimeType)
        {
            case SessionEndTimeType.Fixed:
                eventSession.Reschedule(
                    UtcInstantRange.Create(request.EventSessionDto.StartTime, request.EventSessionDto.EndTime!.Value),
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            case SessionEndTimeType.OpenEnded:
                eventSession.ScheduleOpenEnded(
                    request.EventSessionDto.StartTime,
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            case SessionEndTimeType.RelativeToPrayer when request.EventSessionDto.EndTime is { } endTime:
                eventSession.ScheduleRelativeToPrayer(
                    UtcInstantRange.Create(request.EventSessionDto.StartTime, endTime),
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            case SessionEndTimeType.RelativeToPrayer:
                eventSession.ScheduleRelativeToPrayer(
                    request.EventSessionDto.StartTime,
                    timezone,
                    _scheduleProjectionCalculator);
                break;
            default:
                throw new InvalidOperationException("Event session end time type is not supported.");
        }

        // Auto-link to the matching EventDay by (EventId, LocalStartDate).
        // Returns null when no EventDay exists for this date — EventDayId stays nullable during transition.
        var matchingDay = eventSession.LocalStartDate is not null
            ? await _eventDayRepository.FindByEventAndLocalDateAsync(
                parentEvent.Id, eventSession.LocalStartDate.Value, cancellationToken)
            : null;
        eventSession.EventDayId = matchingDay?.Id;

        try
        {
            eventSession = await _unitOfWork.ExecuteSerializableAsync(async token =>
            {
                EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                    parentEvent.Id,
                    eventSession.LocationId,
                    eventSession.EventLocationId,
                    token);
                eventSession.AssignEventLocation(eventLocation);
                return await _eventSessionRepository.CreateWithRoomOverlapGuardAsync(eventSession, token);
            }, cancellationToken);
        }
        catch (RoomScheduleConflictException ex)
        {
            return BaseCommandResponse.Failure<Guid>(
                "room_schedule_conflict",
                "Event session creation failed.",
                [ex.Message]);
        }

        if (request.EventSessionDto.IslamicAspect != null)
        {
            var islamicAspect = new EventSessionIslamicAspect();
            islamicAspect.EventSessionId = eventSession.Id;
            islamicAspect.EventSession = null;
            ApplyIslamicAspectDto(islamicAspect, request.EventSessionDto.IslamicAspect, request.EventSessionDto.EndTimeType);

            await _eventSessionIslamicAspectRepository.Create(islamicAspect);
        }

        // Template instantiation: copy session template definitions to runtime
        if (request.EventSessionDto.SessionTemplateId.HasValue)
        {
            var sessionTemplate = await _eventSessionTemplateRepository.GetSessionTemplateWithDetails(
                request.EventSessionDto.SessionTemplateId.Value);

            if (sessionTemplate is { IsPublished: true, IsActive: true })
            {
                eventSession.SourceTemplateId = sessionTemplate.Id;
                eventSession.SourceTemplateKey = sessionTemplate.SessionTemplateKey;
                eventSession.SourceTemplateVersion = sessionTemplate.Version;
                eventSession.InstantiatedFromTemplateAt = DateTimeOffset.UtcNow;
                eventSession.LastSyncedFromTemplateAt = DateTimeOffset.UtcNow;
                await _eventSessionRepository.Update(eventSession);

                var instantiationResult = _instantiationService.InstantiateFromSessionTemplate(
                    eventSession.Id,
                    parentEvent.TenantId,
                    sessionTemplate,
                    "system");

                foreach (var runtimeDef in instantiationResult.Definitions)
                {
                    // Clear DefaultOptionId before initial save to avoid FK violation
                    runtimeDef.Definition.DefaultOptionId = null;

                    await _eventSessionCustomPropertyRepository.CreateWithOptions(
                        runtimeDef.Definition,
                        runtimeDef.Options,
                        runtimeDef.DefaultOptionId,
                        cancellationToken);

                    if (runtimeDef.DefaultValue != null)
                    {
                        await _eventSessionCustomPropertyRepository.SetValue(
                            runtimeDef.DefaultValue, cancellationToken);
                    }
                }

                await _projectionUpdater.RefreshForEventSessionAsync(eventSession.Id, cancellationToken);
            }
        }

        return BaseCommandResponse.Success(eventSession.Id, "Event session created successfully.");
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
