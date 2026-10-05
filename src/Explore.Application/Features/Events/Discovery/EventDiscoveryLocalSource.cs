using System.Text.Json;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Specifications.Events;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging;
using Explore.Application.Features.Events.Handlers.Queries;

namespace Explore.Application.Features.Events.Discovery;

/// <summary>Shared native criteria and current disclosure projection for local public discovery.</summary>
public sealed class EventDiscoveryLocalSource
{
    private readonly IEventRepository _eventRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly ILogger<GetEventListRequestHandler> _logger;
    private readonly IModuleService _moduleService;
    private readonly ITenantContext _tenantContext;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantLifecycleAccessService _lifecycle;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ILocationRepository _locationRepository;
    private readonly TimeProvider _clock;
    private readonly ILocationPrivacyGovernanceService _locationPrivacy;
    private readonly IEventLocationDisclosureService _locationDisclosure;

    public EventDiscoveryLocalSource(
        IEventRepository eventRepository,
        IActorRepository actorRepository,
        IObjectStorageService objectStorageService,
        ILogger<GetEventListRequestHandler> logger,
        IModuleService moduleService,
        ITenantContext tenantContext,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantLifecycleAccessService lifecycle,
        IHierarchicalSettingsResolver settingsResolver,
        ILocationRepository locationRepository,
        TimeProvider clock,
        ILocationPrivacyGovernanceService locationPrivacy,
        IEventLocationDisclosureService locationDisclosure)
    {
        _eventRepository = eventRepository;
        _actorRepository = actorRepository;
        _objectStorageService = objectStorageService;
        _logger = logger;
        _moduleService = moduleService;
        _tenantContext = tenantContext;
        _quotaResolver = quotaResolver;
        _lifecycle = lifecycle;
        _settingsResolver = settingsResolver;
        _locationRepository = locationRepository;
        _clock = clock;
        _locationPrivacy = locationPrivacy;
        _locationDisclosure = locationDisclosure;
    }

    public async Task<PaginatedResult<EventListDto>> QueryAsync(GetEventListRequest request, CancellationToken cancellationToken)
    {
        var specification = await PrepareAsync(request, cancellationToken);
        if (specification is null)
            return PaginatedResult<EventListDto>.Create([], 0, request.PageNumber, request.PageSize);
        var (events, totalCount) = await _eventRepository.GetEventsWithDetailsPaged(
            request.PageNumber, request.PageSize, specification, cancellationToken);
        return PaginatedResult<EventListDto>.Create(
            await ProjectAsync(events, specification, cancellationToken),
            totalCount, request.PageNumber, request.PageSize);
    }

    public async Task<EventQuerySpecification?> PrepareAsync(
        GetEventListRequest request, CancellationToken cancellationToken)
    {
        if (!await _lifecycle.IsPublicAsync(_tenantContext.TenantId, cancellationToken))
            return null;

        var ownershipActorId = await ResolveOwnershipActorIdAsync(request);
        if (ownershipActorId == MissingOwnershipActorId)
        {
            return null;
        }

        var criteria = request;
        if (request.AreaId is { } areaId)
        {
            string? rawConfig = await _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.PublicExperience.DiscoveryAreas,
                new SettingContext(TenantId: _tenantContext.TenantId),
                cancellationToken);
            PublicDiscoveryAreasConfig config;
            try
            {
                config = string.IsNullOrWhiteSpace(rawConfig)
                    ? new PublicDiscoveryAreasConfig()
                    : JsonSerializer.Deserialize<PublicDiscoveryAreasConfig>(
                        rawConfig, JsonSerializerOptions.Web)
                        ?? throw new EventDiscoveryUnavailableException();
            }
            catch (JsonException)
            {
                throw new EventDiscoveryUnavailableException();
            }

            Guid[] references = (config.Areas ?? [])
                .SelectMany(area => area.LocationIds ?? [])
                .Distinct()
                .ToArray();
            var tenantLocationIds = (await _locationRepository.GetExistingTenantLocationIdsAsync(
                _tenantContext.TenantId, references, cancellationToken)).ToHashSet();
            if (PublicDiscoveryAreasConfigValidator.Validate(config, tenantLocationIds).Count > 0)
                throw new EventDiscoveryUnavailableException();

            var area = (config.Areas ?? []).SingleOrDefault(area => area.IsActive && area.Id == areaId);
            Guid[] locationIds = (area?.LocationIds ?? [])
                .Where(id => request.LocationIds is null || request.LocationIds.Contains(id))
                .ToArray();
            if (locationIds.Length == 0)
                return null;

            criteria = request with { LocationIds = locationIds };
        }

        bool regionalSearch = criteria.LocationIds is { Count: > 0 };
        if (regionalSearch
            && !(await _locationPrivacy.ResolveAsync(_tenantContext.TenantId, cancellationToken)).IsResolved)
            throw new EventDiscoveryUnavailableException();

        return await BuildSpecificationAsync(criteria, ownershipActorId, cancellationToken);
    }

    public async Task<List<EventListDto>> ProjectAsync(
        IReadOnlyList<Event> events, EventQuerySpecification specification, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, EventLocationDisclosureResult>? locationProjections = null;
        if (specification.Occurrence?.LocationIds is { Count: > 0 })
        {
            var requests = events.Select(entity =>
            {
                var session = entity.Sessions.Single();
                return new EventLocationDisclosureRequest(
                    entity.TenantId,
                    entity.Id,
                    session.EventLocationId ?? throw new EventDiscoveryUnavailableException(),
                    session.RoomId,
                    RequesterUserId: null,
                    EventLocationDisclosurePurpose.Public);
            }).ToArray();
            locationProjections = await _locationDisclosure.ResolveManyAsync(requests, cancellationToken);
            foreach (var location in requests)
            {
                if (!locationProjections.TryGetValue(location.EventLocationId, out var projection)
                    || !projection.DisclosedFields.Contains(EventLocationDisclosureField.City)
                    || !projection.DisclosedFields.Contains(EventLocationDisclosureField.Country))
                    throw new EventDiscoveryUnavailableException();
            }
        }

        var result = events.Select(entity =>
            {
                var dto = EventMapper.ToListItem(entity);
                dto = dto with
                {
                    IsPast = entity.LastSessionEndUtc is not null
                        && entity.LastSessionEndUtc <= specification.Occurrence!.Now
                };
                if (locationProjections is not null)
                {
                    var projection = locationProjections[entity.Sessions.Single().EventLocationId!.Value];
                    dto = dto with
                    {
                        MatchingSession = dto.MatchingSession! with
                        {
                            City = projection.Values!.City,
                            Country = projection.Values.Country
                        }
                    };
                }
                return dto;
            }).ToList();

        // Resolve presigned URLs for images
        foreach (var dto in result)
        {
            dto.FeaturedImageUri = await ResolveImageUrl(dto.FeaturedImageUri);
            dto.ActorProfilePictureUri = await ResolveImageUrl(dto.ActorProfilePictureUri);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetValidityBoundariesAsync(
        IReadOnlyList<Event> events, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, DateTimeOffset>();
        var hasLocations = events.Any(entity => entity.Sessions.Single().EventLocation is not null);
        var governance = hasLocations
            ? await _locationPrivacy.ResolveAsync(_tenantContext.TenantId, cancellationToken)
            : null;
        if (governance is { IsResolved: false })
            throw new EventDiscoveryUnavailableException();
        foreach (var entity in events)
        {
            var session = entity.Sessions.Single();
            DateTimeOffset? earliest = null;
            Include(session.StartTime);
            Include(session.EndTime);
            if (session.EventLocation is { } location)
            {
                Include(PublicEventLocationProjection.ResolveRevealBoundary(
                    location, governance!.DefaultRevealOffset));
            }
            if (earliest is { } boundary)
                result.Add(entity.Id, boundary);

            void Include(DateTimeOffset? instant)
            {
                if (instant > now && (earliest is null || instant < earliest))
                    earliest = instant;
            }
        }
        return result;
    }

    /// <summary>
    /// Builds an <see cref="EventQuerySpecification"/> from the request's filter and sort parameters.
    /// Aspect-specific filters are only applied when the corresponding module is enabled for the current tenant.
    /// </summary>
    private async Task<EventQuerySpecification> BuildSpecificationAsync(
        GetEventListRequest request, Guid? ownershipActorId, CancellationToken cancellationToken)
    {
        var spec = new EventQuerySpecification();
        var hasExplicitDateSearch = request.DateFrom.HasValue || request.DateTo.HasValue;

        spec = spec.And(EventFilter.PubliclyDiscoverable());
        spec = spec.And(EventFilter.Status((int)EventStatusEnum.Published));

        // ===== Core Event filters (always available) =====

        if (ownershipActorId.HasValue)
            spec = spec.And(EventFilter.Actor(ownershipActorId.Value));

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            spec = spec.And(EventFilter.SearchTerm(request.SearchTerm.Trim()));

        if (request.FormatIds is { Count: > 0 })
            spec = spec.And(EventFilter.Formats(request.FormatIds.ToList()));

        if (request.MadhabIds is { Count: > 0 })
            spec = spec.And(EventFilter.Madhabs(request.MadhabIds.ToList()));

        if (request.EventTypeIds is { Count: > 0 })
            spec = spec.And(EventFilter.EventTypes(request.EventTypeIds.ToList()));

        if (request.AudienceGenderIds is { Count: > 0 })
            spec = spec.And(EventFilter.AudienceGenders(request.AudienceGenderIds.ToList()));

        if (request.AudienceAgeIds is { Count: > 0 })
            spec = spec.And(EventFilter.AudienceAges(request.AudienceAgeIds.ToList()));

        if (request.EventStatusIds is { Count: > 0 })
            spec = spec.And(EventFilter.Statuses(request.EventStatusIds.ToList()));


        // ===== Subquery filters (junction tables — always available) =====

        if (request.CategoryId.HasValue)
            spec = spec.And(EventSubqueryFilter.Category(request.CategoryId.Value));

        if (request.IncludedCategoryIds is { Count: > 0 })
        {
            spec = request.CategoryInclusionMode == TagFilterMode.And
                ? spec.And(EventSubqueryFilter.CategoriesIncludedAll(request.IncludedCategoryIds.ToList()))
                : spec.And(EventSubqueryFilter.CategoriesIncludedAny(request.IncludedCategoryIds.ToList()));
        }

        if (request.ExcludedCategoryIds is { Count: > 0 })
        {
            spec = request.CategoryExclusionMode == TagFilterMode.Or
                ? spec.And(EventSubqueryFilter.CategoriesExcludedAny(request.ExcludedCategoryIds.ToList()))
                : spec.And(EventSubqueryFilter.CategoriesExcludedAll(request.ExcludedCategoryIds.ToList()));
        }

        if (request.IncludedTagIds is { Count: > 0 })
        {
            spec = request.InclusionMode == TagFilterMode.And
                ? spec.And(EventSubqueryFilter.TagsIncludedAll(request.IncludedTagIds.ToList()))
                : spec.And(EventSubqueryFilter.TagsIncludedAny(request.IncludedTagIds.ToList()));
        }

        if (request.ExcludedTagIds is { Count: > 0 })
        {
            spec = request.ExclusionMode == TagFilterMode.Or
                ? spec.And(EventSubqueryFilter.TagsExcludedAny(request.ExcludedTagIds.ToList()))
                : spec.And(EventSubqueryFilter.TagsExcludedAll(request.ExcludedTagIds.ToList()));
        }


        if (request.LanguageIds is { Count: > 0 })
            spec = spec.And(EventSubqueryFilter.Languages(request.LanguageIds.ToList()));

        if (request.RegistrationModeIds is { Count: > 0 })
            spec = spec.And(EventSubqueryFilter.RegistrationModes(request.RegistrationModeIds.ToList()));

        // ===== Islamic aspect filters (module-conditional) =====

        var tenantId = _tenantContext.TenantId;
        var hasIslamicAspectFilters = request.GenderModeIds is { Count: > 0 }
            || request.IncludesQuranRecitation is true
            || request.ReferencePrayerIds is { Count: > 0 }
            || request.IslamicPrimaryLanguageIds is { Count: > 0 }
            || request.HasIslamicAspect is true;

        if (hasIslamicAspectFilters &&
            await _moduleService.IsModuleEnabledAsync(tenantId, "Mod_Islamic", cancellationToken))
        {
            if (request.HasIslamicAspect is true)
                spec = spec.And(AspectPresenceFilter.HasIslamicAspect());

            if (request.GenderModeIds is { Count: > 0 })
                spec = spec.And(IslamicAspectFilter.GenderModes(request.GenderModeIds.ToList()));

            if (request.IncludesQuranRecitation is true)
                spec = spec.And(IslamicAspectFilter.IncludesQuranRecitation());

            if (request.ReferencePrayerIds is { Count: > 0 })
                spec = spec.And(IslamicAspectFilter.ReferencePrayers(request.ReferencePrayerIds.ToList()));

            if (request.IslamicPrimaryLanguageIds is { Count: > 0 })
                spec = spec.And(IslamicAspectFilter.PrimaryLanguages(request.IslamicPrimaryLanguageIds.ToList()));
        }

        // ===== Tech aspect filters (module-conditional) =====

        var hasTechAspectFilters = request.SkillLevelId.HasValue
            || request.IsCodingCompetition is true
            || request.IsHackathon is true
            || request.RequiresLaptop is true
            || !string.IsNullOrWhiteSpace(request.TechStackTag)
            || request.HasTechAspect is true;

        if (hasTechAspectFilters &&
            await _moduleService.IsModuleEnabledAsync(tenantId, "Mod_Tech", cancellationToken))
        {
            if (request.HasTechAspect is true)
                spec = spec.And(AspectPresenceFilter.HasTechAspect());

            if (request.SkillLevelId.HasValue)
                spec = spec.And(TechAspectFilter.SkillLevel((SkillLevel)request.SkillLevelId.Value));

            if (request.IsCodingCompetition is true)
                spec = spec.And(TechAspectFilter.IsCodingCompetition());

            if (request.IsHackathon is true)
                spec = spec.And(TechAspectFilter.IsHackathon());

            if (request.RequiresLaptop is true)
                spec = spec.And(TechAspectFilter.RequiresLaptop());

            if (!string.IsNullOrWhiteSpace(request.TechStackTag))
                spec = spec.And(TechAspectFilter.TechStack(request.TechStackTag.Trim()));
        }

        // ===== Custom property projection filters (Layer 3 — tenant-gated) =====

        var hasProjectionFilters = request.CustomPropertyFilters is { Count: > 0 }
            || !string.IsNullOrWhiteSpace(request.CustomPropertySearchTerm);

        if (hasProjectionFilters &&
            await _quotaResolver.GetBoolAsync("custom_properties.projection_discovery_enabled", tenantId, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(request.CustomPropertySearchTerm))
            {
                spec = spec.And(EventCustomPropertyProjectionFilter.GlobalTextSearch(
                    request.CustomPropertySearchTerm.Trim()));
            }

            if (request.CustomPropertyFilters is { Count: > 0 })
            {
                foreach (var criterion in request.CustomPropertyFilters)
                {
                    var filter = MapCriterionToFilter(criterion);
                    if (filter is not null)
                        spec = spec.And(filter);
                }
            }
        }

        // ===== Sorting =====

        var sort = ResolveSortField(request.SortBy) ?? EventSort.Date;
        spec = request.SortDescending ? spec.SortByDescending(sort) : spec.SortBy(sort);

        return spec.WithOccurrence(new EventOccurrenceDiscoveryFilter(
            request.DateFrom,
            request.DateTo,
            request.View ?? (hasExplicitDateSearch ? TemporalView.All : TemporalView.UpcomingAndOngoing),
            request.OperationNow ?? _clock.GetUtcNow(),
            request.LocationIds));
    }

    private static readonly Guid MissingOwnershipActorId = Guid.Empty;

    private async Task<Guid?> ResolveOwnershipActorIdAsync(GetEventListRequest request)
    {
        var tenantId = _tenantContext.TenantId;

        if (request.ActorId.HasValue)
        {
            var actor = await _actorRepository.GetById(request.ActorId.Value);
            return actor is not null && !actor.IsDeleted ? actor.Id : MissingOwnershipActorId;
        }

        if (request.OrganizationId.HasValue)
        {
            var actor = await _actorRepository.GetActorByOrganizationId(request.OrganizationId.Value);
            return actor is not null && !actor.IsDeleted ? actor.Id : MissingOwnershipActorId;
        }

        if (request.GroupId.HasValue)
        {
            var actor = await _actorRepository.GetActorByGroupId(request.GroupId.Value);
            return actor is not null && !actor.IsDeleted ? actor.Id : MissingOwnershipActorId;
        }

        return null;
    }

    /// <summary>
    /// Resolves a sort field name string to an <see cref="EventSort"/> instance.
    /// </summary>
    private static EventSort? ResolveSortField(string? sortBy) =>
        sortBy?.ToLowerInvariant() switch
        {
            "date" => EventSort.Date,
            "title" => EventSort.Title,
            "views" => EventSort.Views,
            "createdat" => EventSort.CreatedAt,
            _ => null
        };

    private static EventCustomPropertyProjectionFilter? MapCriterionToFilter(CustomPropertyFilterCriterion criterion) =>
        criterion.Operator switch
        {
            CustomPropertyFilterOperator.Equals when criterion.Value is not null =>
                EventCustomPropertyProjectionFilter.ExactMatch(criterion.Namespace, criterion.Key, criterion.Value),

            CustomPropertyFilterOperator.Contains when criterion.Value is not null =>
                EventCustomPropertyProjectionFilter.TextSearch(criterion.Namespace, criterion.Key, criterion.Value),

            CustomPropertyFilterOperator.Exists =>
                EventCustomPropertyProjectionFilter.Exists(criterion.Namespace, criterion.Key),

            CustomPropertyFilterOperator.BooleanTrue =>
                EventCustomPropertyProjectionFilter.BooleanTrue(criterion.Namespace, criterion.Key),

            CustomPropertyFilterOperator.OptionEquals when criterion.OptionId.HasValue =>
                EventCustomPropertyProjectionFilter.OptionMatch(criterion.Namespace, criterion.Key, criterion.OptionId.Value),

            CustomPropertyFilterOperator.OptionIn when criterion.OptionIds is { Count: > 0 } =>
                EventCustomPropertyProjectionFilter.OptionsMatchAny(criterion.Namespace, criterion.Key, criterion.OptionIds.ToList()),

            CustomPropertyFilterOperator.NumberRange when criterion.MinNumber.HasValue || criterion.MaxNumber.HasValue =>
                EventCustomPropertyProjectionFilter.NumberRange(criterion.Namespace, criterion.Key, criterion.MinNumber, criterion.MaxNumber),

            CustomPropertyFilterOperator.DateRange when criterion.DateFrom.HasValue || criterion.DateTo.HasValue =>
                EventCustomPropertyProjectionFilter.DateRange(criterion.Namespace, criterion.Key, criterion.DateFrom, criterion.DateTo),

            _ => null
        };

    private Task<string?> ResolveImageUrl(string? objectKeyOrUri)
        => StoragePresentationUrlResolver.ResolveImageUrlAsync(
            objectKeyOrUri,
            _logger,
            "event list image");
}
