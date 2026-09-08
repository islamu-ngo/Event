using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Geocoding;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Lifecycle;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Commands;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IEventSessionSpeakerRepository _eventSessionSpeakerRepository;
    private readonly IEventIslamicAspectRepository _eventIslamicAspectRepository;
    private readonly IEventSessionIslamicAspectRepository _eventSessionIslamicAspectRepository;
    private readonly IEventSessionLanguageRepository _eventSessionLanguageRepository;
    private readonly IEventRoleAssignmentRepository _eventRoleAssignmentRepository;
    private readonly IEventActorResolver _actorResolver;
    private readonly IAudienceAgeRepository _audienceAgeRepository;
    private readonly IAudienceGenderRepository _audienceGenderRepository;
    private readonly IEventTypeRepository _eventTypeRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly IEventRegistrationPolicyRepository _eventRegistrationPolicyRepository;
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IEventCustomPropertyProjectionUpdater _eventCustomPropertyProjectionUpdater;
    private readonly IEventTemplateInstantiationService _eventTemplateInstantiationService;
    private readonly IEventSessionTemplateRepository _eventSessionTemplateRepository;
    private readonly IEventSessionCustomPropertyRepository _eventSessionCustomPropertyRepository;
    private readonly IEventSessionCustomPropertyProjectionUpdater _eventSessionCustomPropertyProjectionUpdater;
    private readonly IEventSessionTemplateInstantiationService _eventSessionTemplateInstantiationService;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IRegistrationModeRepository _registrationModeRepository;
    private readonly ILanguageRepository _languageRepository;
    private readonly IMadhabRepository _madhabRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IScheduleItemKindRepository _scheduleItemKindRepository;
    private readonly IEventSessionKindRepository _eventSessionKindRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IEventDayRepository _eventDayRepository;
    private readonly ILocationRoomRepository _locationRoomRepository;
    private readonly IEventAgendaItemRepository _eventAgendaItemRepository;
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly IAddressGovernancePolicyResolver _addressGovernancePolicyResolver;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;
    private readonly BusinessMetrics _metrics;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IEventLifecyclePolicyProvider _lifecyclePolicyProvider;
    private readonly IEventLifecycleReadinessEvaluator _lifecycleReadinessEvaluator;
    private readonly EventLocationAttachmentService _eventLocationAttachmentService;
    private readonly AtprotoEventPublicationPlanner _atprotoPublicationPlanner;
    private readonly TimeProvider _timeProvider;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IVisitorAccessCapabilityResolver _visitorCapabilities;

    public CreateEventCommandHandler(
        IEventRepository eventRepository,
        IEventSessionRepository eventSessionRepository,
        IEventSessionSpeakerRepository eventSessionSpeakerRepository,
        IEventIslamicAspectRepository eventIslamicAspectRepository,
        IEventSessionIslamicAspectRepository eventSessionIslamicAspectRepository,
        IEventSessionLanguageRepository eventSessionLanguageRepository,
        IEventRoleAssignmentRepository eventRoleAssignmentRepository,
        IEventActorResolver actorResolver,
        IAudienceAgeRepository audienceAgeRepository,
        IAudienceGenderRepository audienceGenderRepository,
        IEventTypeRepository eventTypeRepository,
        IStorageObjectRepository storageObjectRepository,
        IEventTemplateRepository eventTemplateRepository,
        IEventSeriesRepository eventSeriesRepository,
        IEventRegistrationPolicyRepository eventRegistrationPolicyRepository,
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IEventCustomPropertyProjectionUpdater eventCustomPropertyProjectionUpdater,
        IEventTemplateInstantiationService eventTemplateInstantiationService,
        IEventSessionTemplateRepository eventSessionTemplateRepository,
        IEventSessionCustomPropertyRepository eventSessionCustomPropertyRepository,
        IEventSessionCustomPropertyProjectionUpdater eventSessionCustomPropertyProjectionUpdater,
        IEventSessionTemplateInstantiationService eventSessionTemplateInstantiationService,
        IOrganizationRepository organizationRepository,
        IGroupRepository groupRepository,
        ILocationRepository locationRepository,
        IRegistrationModeRepository registrationModeRepository,
        ILanguageRepository languageRepository,
        IMadhabRepository madhabRepository,
        ICategoryRepository categoryRepository,
        ITagRepository tagRepository,
        IScheduleItemKindRepository scheduleItemKindRepository,
        IEventSessionKindRepository eventSessionKindRepository,
        IActorRepository actorRepository,
        IEventDayRepository eventDayRepository,
        ILocationRoomRepository locationRoomRepository,
        IEventAgendaItemRepository eventAgendaItemRepository,
        IEventCategoriesRepository eventCategoriesRepository,
        IEventTagsRepository eventTagsRepository,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        IAddressGovernancePolicyResolver addressGovernancePolicyResolver,
        IUserContext userContext,
        ITenantContext tenantContext,
        HybridCache cache,
        BusinessMetrics metrics,
        IUnitOfWork unitOfWork,
        IOutboxRepository outboxRepository,
        IEventLifecyclePolicyProvider lifecyclePolicyProvider,
        IEventLifecycleReadinessEvaluator lifecycleReadinessEvaluator,
        EventLocationAttachmentService eventLocationAttachmentService,
        AtprotoEventPublicationPlanner atprotoPublicationPlanner,
        TimeProvider timeProvider,
        ISettingMutationLock mutationLock,
        IVisitorAccessCapabilityResolver visitorCapabilities)
    {
        _eventRepository = eventRepository;
        _eventSessionRepository = eventSessionRepository;
        _eventSessionSpeakerRepository = eventSessionSpeakerRepository;
        _eventIslamicAspectRepository = eventIslamicAspectRepository;
        _eventSessionIslamicAspectRepository = eventSessionIslamicAspectRepository;
        _eventSessionLanguageRepository = eventSessionLanguageRepository;
        _eventRoleAssignmentRepository = eventRoleAssignmentRepository;
        _actorResolver = actorResolver;
        _audienceAgeRepository = audienceAgeRepository;
        _audienceGenderRepository = audienceGenderRepository;
        _eventTypeRepository = eventTypeRepository;
        _storageObjectRepository = storageObjectRepository;
        _eventTemplateRepository = eventTemplateRepository;
        _eventSeriesRepository = eventSeriesRepository;
        _eventRegistrationPolicyRepository = eventRegistrationPolicyRepository;
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _eventCustomPropertyProjectionUpdater = eventCustomPropertyProjectionUpdater;
        _eventTemplateInstantiationService = eventTemplateInstantiationService;
        _eventSessionTemplateRepository = eventSessionTemplateRepository;
        _eventSessionCustomPropertyRepository = eventSessionCustomPropertyRepository;
        _eventSessionCustomPropertyProjectionUpdater = eventSessionCustomPropertyProjectionUpdater;
        _eventSessionTemplateInstantiationService = eventSessionTemplateInstantiationService;
        _organizationRepository = organizationRepository;
        _groupRepository = groupRepository;
        _locationRepository = locationRepository;
        _registrationModeRepository = registrationModeRepository;
        _languageRepository = languageRepository;
        _madhabRepository = madhabRepository;
        _categoryRepository = categoryRepository;
        _tagRepository = tagRepository;
        _scheduleItemKindRepository = scheduleItemKindRepository;
        _eventSessionKindRepository = eventSessionKindRepository;
        _actorRepository = actorRepository;
        _eventDayRepository = eventDayRepository;
        _locationRoomRepository = locationRoomRepository;
        _eventAgendaItemRepository = eventAgendaItemRepository;
        _eventCategoriesRepository = eventCategoriesRepository;
        _eventTagsRepository = eventTagsRepository;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _addressGovernancePolicyResolver = addressGovernancePolicyResolver;
        _userContext = userContext;
        _tenantContext = tenantContext;
        _cache = cache;
        _metrics = metrics;
        _unitOfWork = unitOfWork;
        _outboxRepository = outboxRepository;
        _lifecyclePolicyProvider = lifecyclePolicyProvider;
        _lifecycleReadinessEvaluator = lifecycleReadinessEvaluator;
        _eventLocationAttachmentService = eventLocationAttachmentService;
        _atprotoPublicationPlanner = atprotoPublicationPlanner;
        _timeProvider = timeProvider;
        _mutationLock = mutationLock;
        _visitorCapabilities = visitorCapabilities;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var dto = request.EventDto;

        var validationErrors = await ValidateRequestAsync(dto, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationErrors,
                "Event creation failed due to validation errors.");
        }

        int requestedStatusId = dto.EventStatusId == 0 ? (int)EventStatusEnum.Draft : dto.EventStatusId;
        if (requestedStatusId is not ((int)EventStatusEnum.Draft or (int)EventStatusEnum.Published))
        {
            return BaseCommandResponse.Failure<Guid>(
                "event_create_status_not_supported",
                "Event creation failed due to validation errors.",
                ["Event creation supports only Draft or Published status."]);
        }

        var currentUserId = _userContext.GetRequiredUserId();

        Guid?[] imageIds =
        [
            dto.FeaturedImageId,
            dto.BackgroundImageId,
            .. dto.Sessions.Select(session => session.FeaturedImageId),
            .. dto.Days.Select(day => day.BannerImageId)
        ];
        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                _tenantContext.TenantId,
                imageIds))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Every image must be an active public safe-raster object in the current tenant."],
                "Event creation failed due to validation errors.");
        }

        var actorResult = await ResolvePublisherActorAsync(dto, currentUserId, cancellationToken);
        if (!actorResult.Succeeded)
        {
            return BaseCommandResponse.Validation<Guid>(
                [actorResult.ErrorDetail!],
                actorResult.ErrorMessage!);
        }

        List<AddressGovernancePolicyDecision> addressDecisions = [];
        Guid? verifiedOrganizationId = !actorResult.IsCommunitySubmission
            && dto.OrganizationId.HasValue
            && dto.GroupId is null
                ? dto.OrganizationId
                : null;
        try
        {
            foreach (var _ in dto.Locations)
            {
                AddressGovernancePolicyDecision decision = await _addressGovernancePolicyResolver.ResolveAsync(
                    new AddressGovernancePolicyRequest(
                        _tenantContext.TenantId,
                        actorResult.ActorId,
                        currentUserId,
                        verifiedOrganizationId),
                    cancellationToken);
                if (!decision.IsValidManualDecision(verifiedOrganizationId))
                {
                    return AddressGovernanceFailure();
                }

                addressDecisions.Add(decision);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return AddressGovernanceFailure();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var timezoneId = ResolveTimezoneId(dto);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        var createdAt = now;
        DateTime occurredAt = now.UtcDateTime;
        bool publishOnCreate = requestedStatusId == (int)EventStatusEnum.Published;
        var eventEntity = BuildEventEntity(dto, actorResult, timezoneId, currentUserId, createdAt, publishOnCreate);
        var federationOutboxId = Guid.CreateVersion7();
        var notificationFanoutOutboxId = Guid.CreateVersion7();
        var federationCreatedAt = occurredAt;

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var response = await _mutationLock.ExecuteOrderedGroupsAsync(
                [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                token => _unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var capability = await _visitorCapabilities.ResolveAsync(eventEntity.TenantId, ct);
                if (dto.ParticipationConfiguration.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired
                    && !capability.AllowsAccountRequiredParticipation)
                {
                    return BaseCommandResponse.Failure<Guid>("event_visitor_account_onboarding_required",
                        "Account-required participation requires an allowed public onboarding provider.");
                }

                if (publishOnCreate)
                {
                    EventLifecyclePolicy policy = await _lifecyclePolicyProvider.GetEffectivePolicyAsync(
                        eventEntity.TenantId, ValidationProfile.EventPublish, ct);
                    if (policy.RequiresApproval)
                    {
                        return BaseCommandResponse.Failure<Guid>(
                            EventPublicationExecutor.ApprovalRequiredCode,
                            "Event creation failed because approval is required before publication.",
                            ["This event cannot be published directly because tenant approval is required."]);
                    }

                    LifecycleReadinessResult readiness = _lifecycleReadinessEvaluator.Evaluate(eventEntity, policy.Profile, policy);
                    if (!readiness.IsReady)
                    {
                        return BaseCommandResponse.Failure<Guid>(
                            "event_publish_readiness_failed",
                            "Event creation failed because the event is not ready to publish.",
                            readiness.Errors.Select(error => error.Message));
                    }

                    eventEntity.Publish(occurredAt);
                }

                eventEntity = await _eventRepository.Create(eventEntity);
                await AssignFeaturedImageActorAsync(dto, actorResult.ActorId);
                await CreateEventIslamicAspectAsync(dto, eventEntity, ct);
                await AssignInitialEventOwnerAsync(eventEntity, currentUserId, createdAt.UtcDateTime, ct);

                var locationMap = await CreateLocationsAsync(
                    dto,
                    addressDecisions,
                    actorResult.ActorId,
                    occurredAt,
                    ct);
                var dayMaps = await CreateEventDaysAsync(dto, eventEntity, timezoneId, ct);
                var roomMap = await CreateRoomsAsync(dto, locationMap, ct);
                await CreateSessionsAsync(dto, eventEntity, locationMap, dayMaps, roomMap, timezoneId, currentUserId, createdAt, ct);
                await CreateEventAgendaItemsAsync(dto, eventEntity, locationMap, dayMaps, roomMap, timezoneId, ct);
                await CreateCategoryAndTagAssignmentsAsync(dto, eventEntity, ct);
                await InstantiateTemplatePropertiesAsync(dto, eventEntity, currentUserId, createdAt, ct);

                if (publishOnCreate)
                {
                    await _atprotoPublicationPlanner.PlanEventAsync(
                        new AtprotoEventPublicationInput(
                            eventEntity.TenantId,
                            currentUserId,
                            eventEntity.Id,
                            eventEntity.ConcurrencyStamp,
                            PdsSyncOperation.Create,
                            federationOutboxId,
                            federationCreatedAt),
                        ct);
                    var publishedAt = now;
                    await _outboxRepository.Create(EventPublishedOutboxMessageFactory.CreateNotificationFanoutOutboxMessage(
                        notificationFanoutOutboxId,
                        eventEntity,
                        publishedAt));
                }

                return BaseCommandResponse.Success(eventEntity.Id, "Event created successfully.");
            }, token), cancellationToken);
            if (!response.IsSuccess)
            {
                return response;
            }

            Guid eventId = response.Id;
            _metrics.RecordEventCreated();
            try
            {
                await _cache.RemoveAsync($"event:detail:{eventId}", cancellationToken);
                await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(_tenantContext.TenantId), cancellationToken);
            }
            catch (Exception)
            {
                // Best-effort cache invalidation - Redis may be unavailable in local dev
            }

            return BaseCommandResponse.Success(eventId, "Event created successfully.");
        }
        catch (RoomScheduleConflictException ex)
        {
            return BaseCommandResponse.Failure<Guid>(
                "room_schedule_conflict",
                "Event creation failed.",
                [ex.Message]);
        }
    }

    private async Task<List<string>> ValidateRequestAsync(CreateEventDto request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventDtoValidator(
            _audienceAgeRepository,
            _audienceGenderRepository,
            _eventTypeRepository,
            _organizationRepository,
            _groupRepository,
            _storageObjectRepository,
            _eventTemplateRepository,
            _eventSeriesRepository,
            _eventRegistrationPolicyRepository,
            _locationRepository,
            _registrationModeRepository,
            _languageRepository,
            _madhabRepository,
            _categoryRepository,
            _tagRepository,
            _scheduleItemKindRepository,
            _eventSessionKindRepository,
            _locationRoomRepository,
            _eventSessionTemplateRepository,
            _actorRepository);

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        return validationResult.Errors.Select(e => e.ErrorMessage).ToList();
    }

    private Task<EventActorResult> ResolvePublisherActorAsync(CreateEventDto request, Guid currentUserId, CancellationToken cancellationToken) =>
        _actorResolver.ResolveAsync(currentUserId, request.OrganizationId, request.GroupId, cancellationToken);

    private Event BuildEventEntity(
        CreateEventDto dto,
        EventActorResult actorResult,
        string timezoneId,
        Guid currentUserId,
        DateTimeOffset createdAt,
        bool includePublicScheduleRollup)
    {
        var publicSessionRequests = includePublicScheduleRollup
            ? dto.Sessions
            : [];
        var firstSession = publicSessionRequests.MinBy(s => s.StartTime);
        var lastSession = publicSessionRequests.MaxBy(s => s.StartTime);
        var firstSessionLocal = firstSession is null
            ? (DateOnly?)null
            : _scheduleProjectionCalculator.Project(firstSession.StartTime, firstSession.EndTime, timezoneId).LocalStartDate;
        var lastSessionLocal = lastSession is null
            ? (DateOnly?)null
            : _scheduleProjectionCalculator.Project(lastSession.StartTime, lastSession.EndTime, timezoneId).LocalStartDate;

        var eventEntity = new Event(includePublicScheduleRollup
            ? EventStatusEnum.Published
            : EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = dto.Title,
            Subtitle = dto.Subtitle,
            Description = dto.Description,
            Content = dto.Content,
            Slug = string.IsNullOrWhiteSpace(dto.Slug) ? SlugGenerator.FromTitle(dto.Title, "event") : dto.Slug,
            PublicCode = GeneratePublicCode(),
            EventTypeId = dto.EventTypeId,
            AudienceGenderId = dto.AudienceGenderId,
            AudienceAgeId = dto.AudienceAgeId,
            FeaturedImageId = dto.FeaturedImageId,
            VisibilityTypeId = dto.VisibilityTypeId == 0 ? 1 : dto.VisibilityTypeId,
            EventFormatId = dto.EventFormatId == 0 ? 1 : dto.EventFormatId,
            MadhabId = dto.MadhabId ?? dto.IslamicAspect?.MadhabId,
            Timezone = timezoneId,
            EventTimeZoneId = timezoneId,
            BackgroundColor = dto.BackgroundColor,
            BackgroundEffect = dto.BackgroundEffect,
            BackgroundImageId = dto.BackgroundImageId,
            EventSeriesId = dto.EventSeriesId,
            SeriesOrder = dto.SeriesOrder,
            RegistrationPolicyId = dto.RegistrationPolicyId,
            FirstSessionDate = firstSessionLocal,
            LastSessionDate = lastSessionLocal,
            FirstSessionStartUtc = firstSession?.StartTime.ToUniversalTime(),
            LastSessionStartUtc = lastSession?.StartTime.ToUniversalTime(),
            SessionCount = publicSessionRequests.Count,
            ActorId = actorResult.ActorId,
            Actor = null!,
            EventProvenanceTypeId = actorResult.IsCommunitySubmission
                ? (int)EventProvenanceTypeEnum.CommunityReported
                : (int)EventProvenanceTypeEnum.OrganizerCreated,
            SubmittedByUserId = actorResult.IsCommunitySubmission ? currentUserId : null,
            OrganizerActorId = actorResult.IsCommunitySubmission ? null : actorResult.ActorId,
            TenantId = _tenantContext.TenantId,
            Tenant = null!,
            TotalViews = 0,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!,
            CreatedAt = createdAt.UtcDateTime,
            CreatedBy = currentUserId,
            IsDeleted = false
        };

        eventEntity.ParticipationConfiguration = EventParticipationConfiguration.Create(
            eventEntity.Id,
            eventEntity.TenantId,
            dto.ParticipationConfiguration.ParticipationHandlingModeId,
            dto.ParticipationConfiguration.AdvanceRegistrationObligationId,
            dto.ParticipationConfiguration.IdentityAccessModeId,
            dto.ParticipationConfiguration.GuestRecoveryPolicy,
            createdAt.UtcDateTime);
        return eventEntity;
    }

    private static string GeneratePublicCode() => Guid.CreateVersion7().ToString("N")[..12];

    private static BaseCommandResponse<Guid> AddressGovernanceFailure() =>
        BaseCommandResponse.Failure<Guid>(
            "event_location_address_governance_failed",
            "Event creation failed.");

    private async Task AssignFeaturedImageActorAsync(CreateEventDto dto, Guid actorId)
    {
        if (!dto.FeaturedImageId.HasValue) return;

        var storageObject = await _storageObjectRepository.GetById(dto.FeaturedImageId.Value);
        if (storageObject is null) return;

        storageObject.ActorId = actorId;
        await _storageObjectRepository.Update(storageObject);
    }

    private async Task CreateEventIslamicAspectAsync(CreateEventDto dto, Event eventEntity, CancellationToken ct)
    {
        if (dto.IslamicAspect is null) return;

        var aspect = new EventIslamicAspect
        {
            Id = eventEntity.Id,
            Event = null,
            MadhabId = dto.IslamicAspect.MadhabId,
            ReferencePrayer = dto.IslamicAspect.ReferencePrayer,
            PrayerTimeOffset = dto.IslamicAspect.PrayerTimeOffset,
            GenderMode = dto.IslamicAspect.GenderMode,
            IncludesQuranRecitation = dto.IslamicAspect.IncludesQuranRecitation,
            PrimaryLanguageId = dto.IslamicAspect.PrimaryLanguageId
        };

        await _eventIslamicAspectRepository.Upsert(aspect);
    }

    private async Task AssignInitialEventOwnerAsync(
        Explore.Domain.Event eventEntity,
        Guid creatorUserId,
        DateTime createdAtUtc,
        CancellationToken ct)
    {
        if (eventEntity.EventProvenanceTypeId == (int)EventProvenanceTypeEnum.CommunityReported)
        {
            return;
        }

        var assignment = EventRoleAssignment.Create(
            eventEntity.TenantId,
            eventEntity.Id,
            creatorUserId,
            (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active,
            createdAtUtc,
            expiresAtUtc: null,
            createdByUserId: creatorUserId);

        await _eventRoleAssignmentRepository.Create(assignment);
    }

    private async Task<(Dictionary<string, EventDay> ByKey, Dictionary<DateOnly, EventDay> ByDate)> CreateEventDaysAsync(
        CreateEventDto dto,
        Event eventEntity,
        string timezoneId,
        CancellationToken ct)
    {
        var explicitDayByDate = dto.Days
            .GroupBy(d => d.LocalDate)
            .ToDictionary(g => g.Key, g => g.First());

        var sessionDates = dto.Sessions
            .Select(s => _scheduleProjectionCalculator.Project(s.StartTime, s.EndTime, timezoneId).LocalStartDate)
            .Distinct()
            .Order()
            .ToList();

        var shouldCreateSessionDays = sessionDates.Count > 1;
        var datesToCreate = explicitDayByDate.Keys
            .Concat(shouldCreateSessionDays ? sessionDates : Enumerable.Empty<DateOnly>())
            .Distinct()
            .Order()
            .ToList();

        var byKey = new Dictionary<string, EventDay>(StringComparer.OrdinalIgnoreCase);
        var byDate = new Dictionary<DateOnly, EventDay>();
        var sortOrder = 0;

        foreach (var localDate in datesToCreate)
        {
            explicitDayByDate.TryGetValue(localDate, out var requestDay);
            var day = new EventDay
            {
                EventId = eventEntity.Id,
                Event = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!,
                LocalDate = localDate,
                Label = requestDay?.Label,
                Description = requestDay?.Description,
                BannerText = requestDay?.BannerText,
                BannerImageId = requestDay?.BannerImageId,
                IsPublished = requestDay?.IsPublished ?? true,
                SortOrder = requestDay?.SortOrder ?? sortOrder,
                AllowsDayScopeRegistration = requestDay?.AllowsDayScopeRegistration ?? false
            };

            day = await _eventDayRepository.Create(day);
            byDate[localDate] = day;

            if (!string.IsNullOrWhiteSpace(requestDay?.TempKey))
            {
                byKey[requestDay.TempKey.Trim()] = day;
            }

            sortOrder++;
        }

        return (byKey, byDate);
    }

    private async Task<Dictionary<string, Location>> CreateLocationsAsync(
        CreateEventDto dto,
        IReadOnlyList<AddressGovernancePolicyDecision> decisions,
        Guid actorId,
        DateTime changedAtUtc,
        CancellationToken ct)
    {
        var byKey = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < dto.Locations.Count; index++)
        {
            CreateEventLocationDto locationDto = dto.Locations[index];
            AddressGovernancePolicyDecision decision = decisions[index];
            var location = new Location
            {
                FullName = locationDto.FullName,
                Country = locationDto.Country,
                City = locationDto.City,
                Timezone = locationDto.Timezone,
                TenantId = _tenantContext.TenantId,
                Tenant = null!
            };
            location.SetManualAddress(locationDto.Address, locationDto.Postcode);
            location.ApplyAddressGovernanceWithAudit(
                actorId,
                LocationAddressSourceEnum.Manual,
                decision.InitialVisibility,
                decision.AddressOrganizationId,
                changedAtUtc);

            ct.ThrowIfCancellationRequested();
            location = await _locationRepository.Create(location, ct);
            byKey[locationDto.TempKey.Trim()] = location;
        }

        return byKey;
    }

    private async Task<Dictionary<string, LocationRoom>> CreateRoomsAsync(
        CreateEventDto dto,
        IReadOnlyDictionary<string, Location> locationMap,
        CancellationToken ct)
    {
        var byKey = new Dictionary<string, LocationRoom>(StringComparer.OrdinalIgnoreCase);

        foreach (var roomDto in dto.Rooms)
        {
            var locationId = ResolveLocationId(roomDto.LocationTempKey, roomDto.LocationId, locationMap)
                ?? throw new InvalidOperationException("Room location was not resolved.");

            var room = new LocationRoom
            {
                LocationId = locationId,
                Location = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!,
                Name = roomDto.Name,
                Slug = string.IsNullOrWhiteSpace(roomDto.Slug) ? SlugGenerator.FromTitle(roomDto.Name, "room") : roomDto.Slug,
                Description = roomDto.Description,
                Capacity = roomDto.Capacity,
                SortOrder = roomDto.SortOrder
            };

            room = await _locationRoomRepository.Create(room);
            byKey[roomDto.TempKey.Trim()] = room;
        }

        return byKey;
    }

    private async Task CreateSessionsAsync(
        CreateEventDto dto,
        Event eventEntity,
        IReadOnlyDictionary<string, Location> locationMap,
        (Dictionary<string, EventDay> ByKey, Dictionary<DateOnly, EventDay> ByDate) dayMaps,
        IReadOnlyDictionary<string, LocationRoom> roomMap,
        string timezoneId,
        Guid currentUserId,
        DateTimeOffset createdAt,
        CancellationToken ct)
    {
        if (dto.Sessions.Count == 0)
        {
            if (eventEntity.EventStatusId != (int)EventStatusEnum.Draft)
            {
                await CreateDefaultSessionAsync(dto, eventEntity, locationMap, roomMap, timezoneId, ct);
            }
            return;
        }

        var index = 0;
        foreach (var sessionDto in dto.Sessions.OrderBy(s => s.StartTime).ThenBy(s => s.SortOrder))
        {
            index++;
            var session = new EventSession(eventEntity.EventStatusId == (int)EventStatusEnum.Published
                ? EventSessionStatusEnum.Published
                : EventSessionStatusEnum.Draft)
            {
                EventId = eventEntity.Id,
                Event = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!,
                Title = string.IsNullOrWhiteSpace(sessionDto.Title) ? eventEntity.Title : sessionDto.Title,
                Description = sessionDto.Description,
                LocationId = ResolveLocationId(sessionDto.LocationTempKey, sessionDto.LocationId, locationMap, sessionDto.RoomTempKey, roomMap),
                RoomId = ResolveRoomId(sessionDto.RoomTempKey, sessionDto.RoomId, roomMap),
                FeaturedImageId = sessionDto.FeaturedImageId,
                SortOrder = sessionDto.SortOrder == 0 ? index - 1 : sessionDto.SortOrder,
                MaxAudienceAttendees = sessionDto.MaxAudienceAttendees,
                CurrentAudienceAttendees = 0,
                EventSessionKindId = sessionDto.EventSessionKindId,
                RegistrationModeId = sessionDto.RegistrationModeId,
                Slug = string.IsNullOrWhiteSpace(sessionDto.Slug)
                    ? SlugGenerator.FromTitle(sessionDto.Title ?? $"{eventEntity.Title}-session-{index}", "session")
                    : sessionDto.Slug
            };

            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                eventEntity.Id,
                session.LocationId,
                currentEventLocationId: null,
                ct);
            session.AssignEventLocation(eventLocation);

            switch (sessionDto.EndTimeType)
            {
                case SessionEndTimeType.Fixed:
                    session.Reschedule(
                        UtcInstantRange.Create(sessionDto.StartTime, sessionDto.EndTime!.Value),
                        timezoneId,
                        _scheduleProjectionCalculator);
                    break;
                case SessionEndTimeType.OpenEnded:
                    session.ScheduleOpenEnded(
                        sessionDto.StartTime,
                        timezoneId,
                        _scheduleProjectionCalculator);
                    break;
                case SessionEndTimeType.RelativeToPrayer when sessionDto.EndTime is { } endTime:
                    session.ScheduleRelativeToPrayer(
                        UtcInstantRange.Create(sessionDto.StartTime, endTime),
                        timezoneId,
                        _scheduleProjectionCalculator);
                    break;
                case SessionEndTimeType.RelativeToPrayer:
                    session.ScheduleRelativeToPrayer(
                        sessionDto.StartTime,
                        timezoneId,
                        _scheduleProjectionCalculator);
                    break;
                default:
                    throw new InvalidOperationException("Event session end time type is not supported.");
            }
            session.EventDayId = session.LocalStartDate is not null
                ? ResolveDayId(sessionDto.DayTempKey, session.LocalStartDate.Value, dayMaps)
                : null;
            session = await PersistSessionWithRoomGuardAsync(session, ct);

            await CreateSessionAspectsAsync(sessionDto, session, ct);
            await CreateSessionLanguagesAsync(sessionDto, session, ct);
            await CreateSessionSpeakersAsync(sessionDto, session, ct);
            await InstantiateSessionTemplatePropertiesAsync(sessionDto, session, currentUserId, createdAt, ct);
        }
    }

    private async Task CreateDefaultSessionAsync(
        CreateEventDto dto,
        Event eventEntity,
        IReadOnlyDictionary<string, Location> locationMap,
        IReadOnlyDictionary<string, LocationRoom> roomMap,
        string timezoneId,
        CancellationToken ct)
    {
        var roomId = ResolveDefaultRoomId(dto, roomMap);
        var locationId = ResolveDefaultLocationId(dto, locationMap, roomMap, roomId);
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            EventId = eventEntity.Id,
            Event = null!,
            TenantId = _tenantContext.TenantId,
            Tenant = null!,
            Title = eventEntity.Title,
            LocationId = locationId,
            RoomId = roomId,
            SortOrder = 0,
            CurrentAudienceAttendees = 0,
            Slug = SlugGenerator.FromTitle($"{eventEntity.Title}-session-1", "session")
        };

        EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
            eventEntity.Id,
            session.LocationId,
            currentEventLocationId: null,
            ct);
        session.AssignEventLocation(eventLocation);

        session.ReprojectLocalTimes(timezoneId, _scheduleProjectionCalculator);
        await PersistSessionWithRoomGuardAsync(session, ct);
    }

    private async Task<EventSession> PersistSessionWithRoomGuardAsync(EventSession session, CancellationToken ct)
    {
        if (session.RoomId is not null && session.StartTime is not null && session.EndTime is not null)
        {
            var conflicts = await _eventSessionRepository.GetOverlappingSessionsInRoomAsync(
                session.RoomId.Value,
                session.StartTime.Value,
                session.EndTime.Value,
                excludeSessionId: null,
                ct);

            if (conflicts.Count > 0)
            {
                throw new RoomScheduleConflictException(session.RoomId.Value, conflicts.Select(s => s.Id).ToList());
            }
        }

        return await _eventSessionRepository.Create(session);
    }

    private async Task CreateSessionAspectsAsync(CreateEventGraphSessionDto sessionDto, EventSession session, CancellationToken ct)
    {
        if (sessionDto.IslamicAspect is null) return;

        var aspect = new EventSessionIslamicAspect
        {
            EventSessionId = session.Id,
            EventSession = null,
            RequiresWudu = sessionDto.IslamicAspect.RequiresWudu,
            RitualRequirementsJson = sessionDto.IslamicAspect.RitualRequirementsJson
        };
        aspect.ApplyScheduling(
            sessionDto.IslamicAspect.StartTimeType,
            sessionDto.IslamicAspect.ReferencePrayer,
            sessionDto.IslamicAspect.OffsetMinutes);
        aspect.ApplyEndTimeScheduling(
            sessionDto.EndTimeType,
            sessionDto.IslamicAspect.EndReferencePrayer,
            sessionDto.IslamicAspect.EndOffsetMinutes);

        await _eventSessionIslamicAspectRepository.Create(aspect);
    }

    private async Task CreateSessionLanguagesAsync(CreateEventGraphSessionDto sessionDto, EventSession session, CancellationToken ct)
    {
        foreach (var languageId in sessionDto.LanguageIds.Distinct())
        {
            await _eventSessionLanguageRepository.Create(new EventSessionLanguage
            {
                EventSessionId = session.Id,
                EventSession = null!,
                LanguageId = languageId,
                Language = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!
            });
        }
    }

    private async Task CreateSessionSpeakersAsync(CreateEventGraphSessionDto sessionDto, EventSession session, CancellationToken ct)
    {
        foreach (var actorId in sessionDto.SpeakerActorIds.Distinct())
        {
            await _eventSessionSpeakerRepository.Create(new EventSessionSpeaker
            {
                EventSessionId = session.Id,
                EventSession = null!,
                ActorId = actorId,
                Actor = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!
            });
        }
    }

    private async Task CreateEventAgendaItemsAsync(
        CreateEventDto dto,
        Event eventEntity,
        IReadOnlyDictionary<string, Location> locationMap,
        (Dictionary<string, EventDay> ByKey, Dictionary<DateOnly, EventDay> ByDate) dayMaps,
        IReadOnlyDictionary<string, LocationRoom> roomMap,
        string timezoneId,
        CancellationToken ct)
    {
        foreach (var itemDto in dto.AgendaItems.OrderBy(i => i.StartTime).ThenBy(i => i.SortOrder))
        {
            var agendaItem = new EventAgendaItem
            {
                EventId = eventEntity.Id,
                Event = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!,
                Title = itemDto.Title,
                Description = itemDto.Description,
                LocationId = ResolveLocationId(itemDto.LocationTempKey, itemDto.LocationId, locationMap, itemDto.RoomTempKey, roomMap),
                RoomId = ResolveRoomId(itemDto.RoomTempKey, itemDto.RoomId, roomMap),
                KindId = itemDto.KindId,
                SortOrder = itemDto.SortOrder
            };

            EventLocation eventLocation = await _eventLocationAttachmentService.ResolveAsync(
                eventEntity.Id,
                agendaItem.LocationId,
                currentEventLocationId: null,
                ct);
            agendaItem.AssignEventLocation(eventLocation);

            agendaItem.Reschedule(UtcInstantRange.Create(itemDto.StartTime, itemDto.EndTime), timezoneId, _scheduleProjectionCalculator);
            agendaItem.EventDayId = ResolveDayId(itemDto.DayTempKey, agendaItem.LocalStartDate, dayMaps);
            await _eventAgendaItemRepository.Create(agendaItem);
        }
    }

    private async Task CreateCategoryAndTagAssignmentsAsync(CreateEventDto dto, Event eventEntity, CancellationToken ct)
    {
        foreach (var categoryId in dto.CategoryIds.Distinct())
        {
            await _eventCategoriesRepository.Create(new Explore.Domain.EventCategories
            {
                EventId = eventEntity.Id,
                Event = null!,
                CategoryId = categoryId,
                Category = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!
            });
        }

        foreach (var tagId in dto.TagIds.Distinct())
        {
            await _eventTagsRepository.Create(new Explore.Domain.EventTags
            {
                EventId = eventEntity.Id,
                Event = null!,
                TagId = tagId,
                Tag = null!,
                TenantId = _tenantContext.TenantId,
                Tenant = null!
            });
        }
    }

    private async Task InstantiateTemplatePropertiesAsync(CreateEventDto dto, Event eventEntity, Guid currentUserId, DateTimeOffset createdAt, CancellationToken ct)
    {
        if (!dto.TemplateId.HasValue) return;

        var template = await _eventTemplateRepository.GetTemplateWithDetails(dto.TemplateId.Value);
        if (template is not { IsPublished: true, IsActive: true }) return;

        eventEntity.SourceTemplateId = template.Id;
        eventEntity.SourceTemplateKey = template.TemplateKey;
        eventEntity.SourceTemplateVersion = template.Version;
        eventEntity.InstantiatedFromTemplateAt = createdAt;
        eventEntity.LastSyncedFromTemplateAt = createdAt;
        await _eventRepository.Update(eventEntity);

        var instantiationResult = _eventTemplateInstantiationService.InstantiateFromTemplate(
            eventEntity.Id, _tenantContext.TenantId, template, currentUserId.ToString());

        foreach (var defWithOptions in instantiationResult.Definitions)
        {
            defWithOptions.Definition.DefaultOptionId = null;
            await _eventCustomPropertyRepository.CreateWithOptions(
                defWithOptions.Definition,
                defWithOptions.Options,
                defWithOptions.DefaultOptionId,
                ct);

            if (defWithOptions.DefaultValue != null)
            {
                await _eventCustomPropertyRepository.SetValue(defWithOptions.DefaultValue, ct);
            }
        }

        await _eventCustomPropertyProjectionUpdater.RefreshForEventAsync(eventEntity.Id, ct);
    }

    private async Task InstantiateSessionTemplatePropertiesAsync(
        CreateEventGraphSessionDto dto,
        EventSession session,
        Guid currentUserId,
        DateTimeOffset createdAt,
        CancellationToken ct)
    {
        if (!dto.SessionTemplateId.HasValue) return;

        var template = await _eventSessionTemplateRepository.GetSessionTemplateWithDetails(dto.SessionTemplateId.Value);
        if (template is not { IsPublished: true, IsActive: true }) return;

        session.SourceTemplateId = template.Id;
        session.SourceTemplateKey = template.SessionTemplateKey;
        session.SourceTemplateVersion = template.Version;
        session.InstantiatedFromTemplateAt = createdAt;
        session.LastSyncedFromTemplateAt = createdAt;
        await _eventSessionRepository.Update(session);

        var instantiationResult = _eventSessionTemplateInstantiationService.InstantiateFromSessionTemplate(
            session.Id,
            _tenantContext.TenantId,
            template,
            currentUserId.ToString());

        foreach (var runtimeDef in instantiationResult.Definitions)
        {
            runtimeDef.Definition.DefaultOptionId = null;
            await _eventSessionCustomPropertyRepository.CreateWithOptions(
                runtimeDef.Definition,
                runtimeDef.Options,
                runtimeDef.DefaultOptionId,
                ct);

            if (runtimeDef.DefaultValue != null)
            {
                await _eventSessionCustomPropertyRepository.SetValue(runtimeDef.DefaultValue, ct);
            }
        }

        await _eventSessionCustomPropertyProjectionUpdater.RefreshForEventSessionAsync(session.Id, ct);
    }

    private static Guid? ResolveRoomId(string? roomTempKey, Guid? existingRoomId, IReadOnlyDictionary<string, LocationRoom> roomMap)
    {
        if (!string.IsNullOrWhiteSpace(roomTempKey) && roomMap.TryGetValue(roomTempKey.Trim(), out var room))
        {
            return room.Id;
        }

        return existingRoomId;
    }

    private static Guid? ResolveDefaultRoomId(
        CreateEventDto dto,
        IReadOnlyDictionary<string, LocationRoom> roomMap)
    {
        var roomRequest = dto.Rooms
            .OrderBy(room => room.SortOrder)
            .FirstOrDefault();

        return roomRequest is null
            ? null
            : ResolveRoomId(roomRequest.TempKey, null, roomMap);
    }

    private static Guid? ResolveDefaultLocationId(
        CreateEventDto dto,
        IReadOnlyDictionary<string, Location> locationMap,
        IReadOnlyDictionary<string, LocationRoom> roomMap,
        Guid? roomId)
    {
        if (roomId.HasValue)
        {
            var room = roomMap.Values.FirstOrDefault(candidate => candidate.Id == roomId.Value);
            if (room is not null)
            {
                return room.LocationId;
            }
        }

        var locationRequest = dto.Locations.FirstOrDefault();
        if (locationRequest is not null
            && locationMap.TryGetValue(locationRequest.TempKey.Trim(), out var location))
        {
            return location.Id;
        }

        return null;
    }

    private static Guid? ResolveLocationId(
        string? locationTempKey,
        Guid? existingLocationId,
        IReadOnlyDictionary<string, Location> locationMap,
        string? roomTempKey = null,
        IReadOnlyDictionary<string, LocationRoom>? roomMap = null)
    {
        if (!string.IsNullOrWhiteSpace(locationTempKey) && locationMap.TryGetValue(locationTempKey.Trim(), out var location))
        {
            return location.Id;
        }

        if (existingLocationId.HasValue)
        {
            return existingLocationId;
        }

        if (!string.IsNullOrWhiteSpace(roomTempKey) && roomMap?.TryGetValue(roomTempKey.Trim(), out var room) == true)
        {
            return room.LocationId;
        }

        return null;
    }

    private static Guid? ResolveDayId(
        string? dayTempKey,
        DateOnly localDate,
        (Dictionary<string, EventDay> ByKey, Dictionary<DateOnly, EventDay> ByDate) dayMaps)
    {
        if (!string.IsNullOrWhiteSpace(dayTempKey) && dayMaps.ByKey.TryGetValue(dayTempKey.Trim(), out var keyedDay))
        {
            return keyedDay.Id;
        }

        return dayMaps.ByDate.TryGetValue(localDate, out var dateDay) ? dateDay.Id : null;
    }

    private static string ResolveTimezoneId(CreateEventDto dto) =>
        ScheduleTimeZoneResolver.NormalizeOrUtc(
            !string.IsNullOrWhiteSpace(dto.EventTimeZoneId)
                ? dto.EventTimeZoneId
                : dto.Timezone);

}
