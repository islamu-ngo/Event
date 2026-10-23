using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
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
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public class CreateEventSessionCommandHandler : ICommandHandler<CreateEventSessionCommand, BaseCommandResponse<Guid>>
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
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipal;
    private readonly IMachineMutationCommitAuthority _machineAuthority;
    private readonly RequestAuthorization<CreateEventSessionCommand> _authorization;
    private readonly ILogger<CreateEventSessionCommandHandler> _logger;

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
        ITenantContext tenantContext, IUserContext userContext, IAdminContext adminContext,
        RequestAuthorization<CreateEventSessionCommand> authorization,
        IMachinePrincipalAccessor machinePrincipal,
        IMachineMutationCommitAuthority machineAuthority,
        ILogger<CreateEventSessionCommandHandler> logger)
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
        _tenantContext = tenantContext;
        _userContext = userContext;
        _adminContext = adminContext;
        _machinePrincipal = machinePrincipal;
        _machineAuthority = machineAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventSessionCommand request, CancellationToken cancellationToken)
    {
        Guid? userId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        bool machine = _machinePrincipal.IsMachineCaller;
        if (!_userContext.IsAuthenticated || _tenantContext.TenantId == Guid.Empty || !machine && userId is null)
            return BaseCommandResponse.Authentication<Guid>();
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

        var input = request.EventSessionDto;
        var authoritativeRequest = request with { TenantId = _tenantContext.TenantId };
        Guid sessionId = Guid.CreateVersion7();
        DateTime createdAt = DateTime.UtcNow;
        try
        {
            return await _unitOfWork.ExecuteSerializableAsync(async token =>
            {
                await _authorization.AuthorizeAsync(authoritativeRequest, _logger, token);
                var parentEvent = machine
                    ? await _machineAuthority.GetAuthorizedSessionParentAsync(
                        _tenantContext.TenantId, input.EventId, AuthorizationActions.Create, token)
                    : userId is { } humanId
                        ? await _eventSessionRepository.GetAuthorizedParentForMutationAsync(
                            _tenantContext.TenantId, humanId, input.EventId, AuthorizationActions.Create, token)
                        : null;
                if (parentEvent is null)
                    throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Create);
                var currentValidation = await validator.ValidateAsync(input, token);
                if (!currentValidation.IsValid)
                    return BaseCommandResponse.Validation<Guid>(
                        currentValidation.Errors.Select(error => error.ErrorMessage), "Event session creation failed.");
                if (!await ImageReferenceEligibility.AreEligibleAsync(_storageObjectRepository, parentEvent.TenantId, input.FeaturedImageId))
                    return BaseCommandResponse.Validation<Guid>(
                        ["Featured image must be an active public safe-raster object in the current tenant."], "Event session creation failed.");
                var eventSession = new EventSession(EventSessionStatusEnum.Draft)
                {
                    Id = sessionId,
                    CreatedAt = createdAt,
                    CreatedBy = userId,
                    EventId = parentEvent.Id,
                    TenantId = parentEvent.TenantId,
                    Event = null!,
                    Tenant = null!,
                    LocationId = input.LocationId,
                    RoomId = input.RoomId,
                    FeaturedImageId = input.FeaturedImageId,
                    SortOrder = input.SortOrder,
                    Title = input.Title,
                    EventSessionKindId = input.EventSessionKindId,
                    Description = input.Description,
                    Slug = input.Slug,
                    MaxAudienceAttendees = input.MaxAudienceAttendees,
                    RegistrationModeId = input.RegistrationModeId,
                    CurrentAudienceAttendees = 0
                };
                string timezone = parentEvent.EventTimeZoneId ?? parentEvent.Timezone ?? string.Empty;
                switch (input.EndTimeType)
                {
                    case SessionEndTimeType.Fixed:
                        eventSession.Reschedule(UtcInstantRange.Create(input.StartTime, input.EndTime!.Value), timezone, _scheduleProjectionCalculator);
                        break;
                    case SessionEndTimeType.OpenEnded:
                        eventSession.ScheduleOpenEnded(input.StartTime, timezone, _scheduleProjectionCalculator);
                        break;
                    case SessionEndTimeType.RelativeToPrayer when input.EndTime is { } endTime:
                        eventSession.ScheduleRelativeToPrayer(UtcInstantRange.Create(input.StartTime, endTime), timezone, _scheduleProjectionCalculator);
                        break;
                    case SessionEndTimeType.RelativeToPrayer:
                        eventSession.ScheduleRelativeToPrayer(input.StartTime, timezone, _scheduleProjectionCalculator);
                        break;
                    default:
                        throw new InvalidOperationException("Event session end time type is not supported.");
                }
                var matchingDay = eventSession.LocalStartDate is { } localDate
                    ? await _eventDayRepository.FindByEventAndLocalDateAsync(parentEvent.Id, localDate, token)
                    : null;
                eventSession.EventDayId = matchingDay?.Id;
                EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                    parentEvent.Id,
                    eventSession.LocationId,
                    eventSession.EventLocationId,
                    token);
                eventSession.AssignEventLocation(eventLocation);
                await _eventSessionRepository.CreateWithRoomOverlapGuardAsync(eventSession, token, machine ? async checkToken =>
                {
                    if (await _machineAuthority.GetAuthorizedSessionParentAsync(
                            parentEvent.TenantId, parentEvent.Id, AuthorizationActions.Create, checkToken) is null)
                        throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Create);
                }
                : null);
                if (input.IslamicAspect is not null)
                {
                    var aspect = new EventSessionIslamicAspect { EventSessionId = eventSession.Id, EventSession = null };
                    ApplyIslamicAspectDto(aspect, input.IslamicAspect, input.EndTimeType);
                    await _eventSessionIslamicAspectRepository.Create(aspect);
                }
                if (input.SessionTemplateId is { } templateId)
                {
                    var template = await _eventSessionTemplateRepository.GetSessionTemplateWithDetails(templateId, token);
                    if (template is { IsPublished: true, IsActive: true })
                    {
                        eventSession.SourceTemplateId = template.Id;
                        eventSession.SourceTemplateKey = template.SessionTemplateKey;
                        eventSession.SourceTemplateVersion = template.Version;
                        eventSession.InstantiatedFromTemplateAt = createdAt;
                        eventSession.LastSyncedFromTemplateAt = createdAt;
                        await _eventSessionRepository.Update(eventSession);
                        var instantiation = _instantiationService.InstantiateFromSessionTemplate(
                            eventSession.Id, parentEvent.TenantId, template, _userContext.GetRequiredUserId().ToString("D"));
                        foreach (var runtime in instantiation.Definitions)
                        {
                            runtime.Definition.DefaultOptionId = null;
                            await _eventSessionCustomPropertyRepository.CreateWithOptions(
                                runtime.Definition, runtime.Options, runtime.DefaultOptionId, token);
                            if (runtime.DefaultValue is not null)
                                await _eventSessionCustomPropertyRepository.SetValue(runtime.DefaultValue, token);
                        }
                        await _projectionUpdater.RefreshForEventSessionAsync(eventSession.Id, token);
                    }
                }
                if (machine && await _machineAuthority.GetAuthorizedSessionParentAsync(
                        parentEvent.TenantId, parentEvent.Id, AuthorizationActions.Create, token) is null)
                    throw new AuthorizationException(ResourceKinds.EventSession, AuthorizationActions.Create);
                token.ThrowIfCancellationRequested();
                return BaseCommandResponse.Success(eventSession.Id, "Event session created successfully.");
            }, cancellationToken);
        }
        catch (RoomScheduleConflictException ex)
        {
            return BaseCommandResponse.Failure<Guid>(
                "room_schedule_conflict",
                "Event session creation failed.",
                [ex.Message]);
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
