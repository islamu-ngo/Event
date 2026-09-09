using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Models.Events;

namespace Explore.Blazor.Client.Services;

public interface IEventService
{
    Task<ICollection<EventListDto>> GetAllEventsAsync();
    Task<ICollection<EventListDto>> GetMyEventsAsync(CancellationToken cancellationToken = default);
    Task<PaginatedResult<EventListDto>> GetEventsPagedAsync(int pageNumber, int pageSize);
    Task<PaginatedResult<EventListDto>> GetEventsPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm = null,
        Guid? categoryId = null,
        List<Guid>? includedCategoryIds = null,
        List<Guid>? excludedCategoryIds = null,
        string? categoryInclusionMode = null,
        string? categoryExclusionMode = null,
        List<Guid>? includedTagIds = null,
        List<Guid>? excludedTagIds = null,
        string? inclusionMode = null,
        string? exclusionMode = null,
        List<int>? formatIds = null,
        List<int>? madhabIds = null,
        List<int>? registrationModeIds = null,
        List<int>? languageIds = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? sortBy = null,
        bool? sortDescending = null,
        List<int>? eventTypeIds = null,
        List<int>? audienceGenderIds = null,
        List<int>? audienceAgeIds = null,
        List<int>? eventStatusIds = null,
        // Islamic filters
        List<int>? genderModeIds = null,
        bool? includesQuranRecitation = null,
        List<int>? referencePrayerIds = null,
        List<int>? islamicPrimaryLanguageIds = null,
        bool? hasIslamicAspect = null,
        // Tech filters
        int? skillLevelId = null,
        bool? isCodingCompetition = null,
        bool? isHackathon = null,
        bool? requiresLaptop = null,
        string? techStackTag = null,
        bool? hasTechAspect = null,
        Guid? actorId = null,
        Guid? organizationId = null,
        Guid? groupId = null,
        string? view = null,
        CancellationToken cancellationToken = default);
    Task<PaginatedResult<EventListDto>> GetManagedEventsByActorAsync(
        Guid actorId,
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);
    Task<PaginatedResult<EventListDto>> GetMyEventsPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<EventDto?> GetEventByIdAsync(Guid eventId);
    Task<EventDto?> GetEventBySlugCodeAsync(string slugCode);
    Task<EventCreationContextDto?> GetEventCreationContextAsync(CancellationToken cancellationToken = default);
    Task<EventProgramSummaryDto?> GetEventProgramSummaryAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<EventProgramSummaryDto?> GetManagedEventProgramSummaryAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<EventPublishReadinessDto?> GetEventPublishReadinessAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<ICollection<HalResourceOfEventPublicActionDto>> GetEventPublicActionsAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ConfigureEventParticipationAsync(
        Guid eventId,
        ConfigureEventParticipationDto configuration,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteEventAsync(Guid eventId);
    Task<BaseCommandResponseOfGuid?> UpdateEventAsync(Guid eventId, EventDraftEditModel request);
    Task<BaseCommandResponseOfGuid?> CreateEventAsync(CreateEventDraftRequestDto request, string? idempotencyKey = null);
    Task<BaseCommandResponseOfGuid?> PublishEventAsync(Guid eventId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> ArchiveEventAsync(Guid eventId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> CancelEventAsync(Guid eventId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);
    Task<ICollection<EventListDto>> GetProfileEventsByActorAsync(Guid actorId);
    Task<ICollection<EventListDto>> GetPublicEventsByActorAsync(Guid actorId);
    Task<ICollection<EventListDto>> GetPublicEventsByOrganizationAsync(Guid organizationId);
    Task<ICollection<EventListDto>> GetPublicEventsByGroupAsync(Guid groupId);
}

public partial class EventService : IEventService
{
    private readonly IEventClient _apiClient;
    private readonly IEventLifecycleClient _lifecycleClient;
    private readonly IEventManagementReadClient _managementReadClient;
    private readonly IEventParticipationClient _participationClient;
    private readonly IEventPublicActionClient _publicActionClient;
    private readonly ILogger<EventService> _logger;

    public EventService(
        IEventClient apiClient,
        IEventLifecycleClient lifecycleClient,
        IEventManagementReadClient managementReadClient,
        IEventParticipationClient participationClient,
        IEventPublicActionClient publicActionClient,
        ILogger<EventService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _lifecycleClient = lifecycleClient ?? throw new ArgumentNullException(nameof(lifecycleClient));
        _managementReadClient = managementReadClient ?? throw new ArgumentNullException(nameof(managementReadClient));
        _participationClient = participationClient ?? throw new ArgumentNullException(nameof(participationClient));
        _publicActionClient = publicActionClient ?? throw new ArgumentNullException(nameof(publicActionClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ICollection<EventListDto>> GetMyEventsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _apiClient.GetMyEventsAsync(1, 100, cancellationToken: cancellationToken);
            return result?.GetItems() ?? new List<EventListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching my events");
            return new List<EventListDto>();
        }
    }

    public async Task<ICollection<EventListDto>> GetAllEventsAsync()
    {
        try
        {
            var result = await _apiClient.GetEventsAsync(1, 100);
            return result?.GetItems() ?? new List<EventListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all events");
            return new List<EventListDto>();
        }
    }

    public async Task<PaginatedResult<EventListDto>> GetEventsPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await _apiClient.GetEventsAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching paged events (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<EventListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<PaginatedResult<EventListDto>> GetEventsPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm = null,
        Guid? categoryId = null,
        List<Guid>? includedCategoryIds = null,
        List<Guid>? excludedCategoryIds = null,
        string? categoryInclusionMode = null,
        string? categoryExclusionMode = null,
        List<Guid>? includedTagIds = null,
        List<Guid>? excludedTagIds = null,
        string? inclusionMode = null,
        string? exclusionMode = null,
        List<int>? formatIds = null,
        List<int>? madhabIds = null,
        List<int>? registrationModeIds = null,
        List<int>? languageIds = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? sortBy = null,
        bool? sortDescending = null,
        List<int>? eventTypeIds = null,
        List<int>? audienceGenderIds = null,
        List<int>? audienceAgeIds = null,
        List<int>? eventStatusIds = null,
        // Islamic filters
        List<int>? genderModeIds = null,
        bool? includesQuranRecitation = null,
        List<int>? referencePrayerIds = null,
        List<int>? islamicPrimaryLanguageIds = null,
        bool? hasIslamicAspect = null,
        // Tech filters
        int? skillLevelId = null,
        bool? isCodingCompetition = null,
        bool? isHackathon = null,
        bool? requiresLaptop = null,
        string? techStackTag = null,
        bool? hasTechAspect = null,
        Guid? actorId = null,
        Guid? organizationId = null,
        Guid? groupId = null,
        string? view = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Sanitize empty lists to null to prevent NSwag URL builder corruption.
            var safeIncludedCatIds = includedCategoryIds is { Count: > 0 } ? includedCategoryIds : null;
            var safeExcludedCatIds = excludedCategoryIds is { Count: > 0 } ? excludedCategoryIds : null;
            var safeIncludedTagIds = includedTagIds is { Count: > 0 } ? includedTagIds : null;
            var safeExcludedTagIds = excludedTagIds is { Count: > 0 } ? excludedTagIds : null;
            var safeFormatIds = formatIds is { Count: > 0 } ? formatIds : null;
            var safeMadhabIds = madhabIds is { Count: > 0 } ? madhabIds : null;
            var safeRegistrationModeIds = registrationModeIds is { Count: > 0 } ? registrationModeIds : null;
            var safeLanguageIds = languageIds is { Count: > 0 } ? languageIds : null;
            var safeEventTypeIds = eventTypeIds is { Count: > 0 } ? eventTypeIds : null;
            var safeAudienceGenderIds = audienceGenderIds is { Count: > 0 } ? audienceGenderIds : null;
            var safeAudienceAgeIds = audienceAgeIds is { Count: > 0 } ? audienceAgeIds : null;
            var safeEventStatusIds = eventStatusIds is { Count: > 0 } ? eventStatusIds : null;
            var safeGenderModeIds = genderModeIds is { Count: > 0 } ? genderModeIds : null;
            var safeReferencePrayerIds = referencePrayerIds is { Count: > 0 } ? referencePrayerIds : null;
            var safeIslamicPrimaryLanguageIds = islamicPrimaryLanguageIds is { Count: > 0 } ? islamicPrimaryLanguageIds : null;

            // Only send mode strings when the corresponding ID list is non-empty
            var safeCatIncMode = safeIncludedCatIds != null ? categoryInclusionMode : null;
            var safeCatExcMode = safeExcludedCatIds != null ? categoryExclusionMode : null;
            var safeTagIncMode = safeIncludedTagIds != null ? inclusionMode : null;
            var safeTagExcMode = safeExcludedTagIds != null ? exclusionMode : null;

            var result = await _apiClient.GetEventsAsync(
                pageNumber: pageNumber,
                pageSize: pageSize,
                searchTerm: searchTerm,
                actorId: actorId,
                organizationId: organizationId,
                groupId: groupId,
                categoryId: categoryId,
                includedCategoryIds: safeIncludedCatIds,
                excludedCategoryIds: safeExcludedCatIds,
                categoryInclusionMode: safeCatIncMode,
                categoryExclusionMode: safeCatExcMode,
                includedTagIds: safeIncludedTagIds,
                excludedTagIds: safeExcludedTagIds,
                inclusionMode: safeTagIncMode,
                exclusionMode: safeTagExcMode,
                formatIds: safeFormatIds,
                madhabIds: safeMadhabIds,
                registrationModeIds: safeRegistrationModeIds,
                languageIds: safeLanguageIds,
                dateFrom: dateFrom,
                dateTo: dateTo,
                sortBy: sortBy,
                sortDescending: sortDescending,
                eventTypeIds: safeEventTypeIds,
                audienceGenderIds: safeAudienceGenderIds,
                audienceAgeIds: safeAudienceAgeIds,
                eventStatusIds: safeEventStatusIds,
                genderModeIds: safeGenderModeIds,
                includesQuranRecitation: includesQuranRecitation,
                referencePrayerIds: safeReferencePrayerIds,
                islamicPrimaryLanguageIds: safeIslamicPrimaryLanguageIds,
                hasIslamicAspect: hasIslamicAspect,
                skillLevelId: skillLevelId,
                isCodingCompetition: isCodingCompetition,
                isHackathon: isHackathon,
                requiresLaptop: requiresLaptop,
                techStackTag: techStackTag,
                hasTechAspect: hasTechAspect,
                view: view,
                cancellationToken: cancellationToken);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching filtered paged events (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<EventListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<PaginatedResult<EventListDto>> GetManagedEventsByActorAsync(
        Guid actorId,
        int pageNumber = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _managementReadClient.GetManagedEventsByActorAsync(
                actorId,
                pageNumber,
                pageSize,
                cancellationToken: cancellationToken);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching managed events for actor {ActorId}", actorId);
            return PaginatedResult<EventListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<PaginatedResult<EventListDto>> GetMyEventsPagedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _apiClient.GetMyEventsAsync(
                pageNumber,
                pageSize,
                cancellationToken: cancellationToken);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching my paged events (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<EventListDto>.Empty(pageNumber, pageSize);
        }
    }


    public async Task<EventDto?> GetEventByIdAsync(Guid eventId)
    {
        try
        {
            var result = await _apiClient.GetEventByIdAsync(eventId);
            return result?.ToDto();
        }
        catch (ApiException<ProblemDetails> ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Public event detail hidden or missing for event {EventId}; trying authorized management detail.", eventId);
            return await GetEventManagementDetailsAsync(eventId);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Public event detail hidden or missing for event {EventId}; trying authorized management detail.", eventId);
            return await GetEventManagementDetailsAsync(eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching event {EventId}", eventId);
            return null;
        }
    }

    public async Task<EventDto?> GetEventBySlugCodeAsync(string slugCode)
    {
        try
        {
            var result = await _apiClient.GetEventByPublicCodeAsync(slugCode);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Public event detail hidden or missing for slug-code {SlugCode}.", slugCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching public event {SlugCode}", slugCode);
            return null;
        }
    }

    private async Task<EventDto?> GetEventManagementDetailsAsync(Guid eventId)
    {
        try
        {
            var result = await _managementReadClient.GetEventManagementDetailsAsync(eventId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode is 401 or 403 or 404)
        {
            _logger.LogDebug(
                "Authorized management event detail was not available for event {EventId}; status {StatusCode}.",
                eventId,
                ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching authorized management event detail {EventId}", eventId);
            return null;
        }
    }

    public async Task<EventCreationContextDto?> GetEventCreationContextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _managementReadClient.GetEventCreationContextAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching event creation context");
            return null;
        }
    }


    public async Task<EventProgramSummaryDto?> GetEventProgramSummaryAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _managementReadClient.GetEventProgramSummaryAsync(eventId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching event program summary {EventId}", eventId);
            return null;
        }
    }

    public async Task<EventProgramSummaryDto?> GetManagedEventProgramSummaryAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _managementReadClient.GetManagedEventProgramSummaryAsync(eventId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching managed event program summary {EventId}", eventId);
            return null;
        }
    }

    public async Task<EventPublishReadinessDto?> GetEventPublishReadinessAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _managementReadClient.GetEventPublishReadinessAsync(eventId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching event publish readiness {EventId}", eventId);
            return null;
        }
    }

    public async Task<bool> DeleteEventAsync(Guid eventId)
    {
        try
        {
            await _lifecycleClient.DeleteEventAsync(eventId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId}", eventId);
            return false;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateEventAsync(Guid eventId, EventDraftEditModel request)
    {
        try
        {
            return await _lifecycleClient.UpdateEventAsync(
                eventId,
                BuildGroupedEventUpdate(request),
                $"\"{request.ExpectedConcurrencyStamp:D}\"");
        }
        catch (ApiException<ProblemDetails> ex) when (ex.StatusCode == 409)
        {
            _logger.LogWarning(ex, "Event update rejected as stale for event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = ex.Result?.Detail ?? ex.Result?.Title ?? "Event changed since it was loaded.",
                Errors = ["Refresh the event and try again."],
                FailureCode = "event_concurrency_conflict"
            };
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            _logger.LogWarning(ex, "Event update rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId}", eventId);
            return null;
        }
    }

    private static UpdateEventDto BuildGroupedEventUpdate(EventDraftEditModel request) => new()
    {
        Title = new UpdateEventTitleDto { Value = request.Title },
        Subtitle = new UpdateEventSubtitleDto { Value = OptionalString(request.Subtitle) },
        Description = new UpdateEventDescriptionDto { Value = OptionalString(request.Description) },
        Content = new UpdateEventContentDto { Value = OptionalString(request.Content) },
        Slug = new UpdateEventSlugDto { Value = OptionalString(request.Slug) },
        EventType = new UpdateEventEventTypeDto { Value = OptionalInt(request.EventTypeId) },
        AudienceGender = new UpdateEventAudienceGenderDto { Value = OptionalInt(request.AudienceGenderId) },
        AudienceAge = new UpdateEventAudienceAgeDto { Value = OptionalInt(request.AudienceAgeId) },
        FeaturedImage = new UpdateEventFeaturedImageDto { Value = OptionalGuid(request.FeaturedImageId) },
        Visibility = new UpdateEventVisibilityDto { Value = request.VisibilityTypeId.GetValueOrDefault(1) },
        Format = new UpdateEventFormatDto { Value = request.EventFormatId.GetValueOrDefault(1) },
        Madhab = new UpdateEventMadhabDto { Value = OptionalInt(request.MadhabId) },
        Timezone = new UpdateEventTimezoneDto { Value = OptionalString(request.Timezone) },
        EventTimeZone = new UpdateEventEventTimeZoneDto { Value = OptionalString(request.EventTimeZoneId) },
        BackgroundColor = new UpdateEventBackgroundColorDto { Value = OptionalString(request.BackgroundColor) },
        BackgroundEffect = new UpdateEventBackgroundEffectDto { Value = OptionalString(request.BackgroundEffect) },
        BackgroundImage = new UpdateEventBackgroundImageDto { Value = OptionalGuid(request.BackgroundImageId) },
        SourceTemplate = new UpdateEventSourceTemplateDto { Value = OptionalGuid(request.TemplateId) },
        SeriesMembership = new UpdateEventSeriesMembershipDto { Value = OptionalGuid(request.EventSeriesId) },
        SeriesOrder = new UpdateEventSeriesOrderDto { Value = OptionalInt(request.SeriesOrder) },
        RegistrationPolicy = new UpdateEventRegistrationPolicyDto { Value = OptionalInt(request.RegistrationPolicyId) }
    };

    private static OptionalUpdateOfstring OptionalString(string? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static OptionalUpdateOfint OptionalInt(int? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static OptionalUpdateOfGuid OptionalGuid(Guid? value) => new()
    {
        HasValue = true,
        Value = value
    };

    public async Task<BaseCommandResponseOfGuid?> ArchiveEventAsync(Guid eventId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _lifecycleClient.ArchiveEventAsync(
                eventId,
                new ArchiveEventRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            _logger.LogWarning(ex, "Event archive rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving event {EventId}", eventId);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CancelEventAsync(Guid eventId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _lifecycleClient.CancelEventAsync(
                eventId,
                new CancelEventRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            _logger.LogWarning(ex, "Event cancel rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling event {EventId}", eventId);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateEventAsync(CreateEventDraftRequestDto request, string? idempotencyKey = null)
    {
        try
        {
            request.CategoryIds ??= [];
            request.TagIds ??= [];
            request.Locations ??= [];
            request.Sessions ??= [];
            request.Days ??= [];
            request.Rooms ??= [];
            request.AgendaItems ??= [];

            return idempotencyKey is { Length: > 0 }
                ? await _lifecycleClient.CreateEventWithIdempotencyKeyAsync(request, idempotencyKey)
                : await _lifecycleClient.CreateEventAsync(request);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            _logger.LogWarning(ex, "Event creation rejected by API validation");
            return ex.Result;
        }
        catch (ApiException<ProblemDetails> ex)
        {
            _logger.LogWarning(ex, "Event creation rejected with problem details");
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = ex.Result?.Detail ?? ex.Result?.Title ?? "Event could not be created."
            };
        }
        catch (ApiException<ValidationProblemDetails>)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> PublishEventAsync(Guid eventId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _lifecycleClient.PublishEventAsync(
                eventId,
                new PublishEventRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            _logger.LogWarning(ex, "Event publish rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing event {EventId}", eventId);
            return null;
        }
    }

    private static EventTicketPriceSummaryDto? ToEventListTicketPriceSummary(TicketPriceSummary? summary) =>
        summary is null
            ? null
            : new EventTicketPriceSummaryDto
            {
                SummaryCode = summary.SummaryCode,
                CurrencyCode = summary.CurrencyCode,
                CurrencyMinorUnitDigits = summary.CurrencyMinorUnitDigits,
                FromAmountMinor = summary.FromAmountMinor
            };

    private static EventParticipationConfigurationDto? ToEventListParticipationConfiguration(ParticipationConfiguration? configuration) =>
        configuration is null
            ? null
            : new EventParticipationConfigurationDto
            {
                EventId = configuration.EventId,
                ConcurrencyStamp = configuration.ConcurrencyStamp,
                ParticipationHandlingModeId = configuration.ParticipationHandlingModeId,
                ParticipationHandlingModeCode = configuration.ParticipationHandlingModeCode,
                ParticipationHandlingModeName = configuration.ParticipationHandlingModeName,
                AdvanceRegistrationObligationId = configuration.AdvanceRegistrationObligationId,
                AdvanceRegistrationObligationCode = configuration.AdvanceRegistrationObligationCode,
                AdvanceRegistrationObligationName = configuration.AdvanceRegistrationObligationName,
                IdentityAccessModeId = configuration.IdentityAccessModeId,
                IdentityAccessModeCode = configuration.IdentityAccessModeCode,
                IdentityAccessModeName = configuration.IdentityAccessModeName,
                GuestRecoveryPolicy = (GuestRecoveryPolicyEnum?)configuration.GuestRecoveryPolicy
            };

    public async Task<ICollection<HalResourceOfEventPublicActionDto>> GetEventPublicActionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var response = await _publicActionClient.GetEventPublicActionsAsync(eventId, cancellationToken: cancellationToken);
        return response._embedded?.Items ?? [];
    }

    public async Task<BaseCommandResponseOfGuid> ConfigureEventParticipationAsync(
        Guid eventId,
        ConfigureEventParticipationDto configuration,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty || expectedConcurrencyStamp == Guid.Empty)
        {
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = "Event and configuration concurrency metadata are required.",
                FailureCode = "participation_configuration_metadata_missing"
            };
        }

        try
        {
            return await _participationClient.ConfigureEventParticipationAsync(
                eventId,
                $"\"{expectedConcurrencyStamp:D}\"",
                configuration,
                cancellationToken: cancellationToken);
        }
        catch (ApiException<ValidationProblemDetails> ex) when (ex.StatusCode == 400)
        {
            _logger.LogWarning("Participation configuration validation failed for event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = ex.Result?.Detail ?? ex.Result?.Title ?? "Participation configuration is invalid.",
                Errors = ex.Result?.Errors?.SelectMany(error => error.Value).ToList() ?? [],
                FailureCode = ex.Result?.AdditionalProperties.TryGetValue("code", out var code) == true &&
                    code is JsonElement { ValueKind: JsonValueKind.String } value
                    ? value.GetString()
                    : "participation_configuration_validation_failed"
            };
        }
        catch (ApiException<ProblemDetails> ex) when (ex.StatusCode == 409)
        {
            _logger.LogWarning("Participation configuration update was stale for event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = ex.Result?.Detail ?? ex.Result?.Title ?? "Participation configuration changed since it was loaded.",
                Errors = ["Refresh the event and try again."],
                FailureCode = "event_participation_configuration_concurrency_conflict"
            };
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Participation configuration update failed for event {EventId} with status {StatusCode}", eventId, ex.StatusCode);
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = ex.StatusCode switch
                {
                    401 => "Sign in again before saving participation configuration.",
                    403 => "You are not authorized to configure participation for this event.",
                    404 => "This event is no longer available.",
                    _ => "Participation configuration could not be saved."
                },
                FailureCode = "participation_configuration_save_failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring participation for event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = "Participation configuration could not be saved.",
                FailureCode = "participation_configuration_save_failed"
            };
        }
    }

    public async Task<ICollection<EventListDto>> GetPublicEventsByActorAsync(Guid actorId)
    {
        try
        {
            var result = await _apiClient.GetEventsAsync(
                pageNumber: 1,
                pageSize: 100,
                actorId: actorId,
                view: "All");
            return result?.GetItems() ?? new List<EventListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching public events for actor {ActorId}", actorId);
            return new List<EventListDto>();
        }
    }

    public async Task<ICollection<EventListDto>> GetProfileEventsByActorAsync(Guid actorId)
    {
        var publicEvents = await GetPublicEventsByActorAsync(actorId);

        try
        {
            var result = await _managementReadClient.GetManagedEventsByActorAsync(
                actorId: actorId,
                pageNumber: 1,
                pageSize: 100);
            var managedEvents = result?.GetItems() ?? new List<EventListDto>();
            return MergeProfileEvents(publicEvents, managedEvents);
        }
        catch (ApiException ex) when (ex.StatusCode is 401 or 403 or 404)
        {
            _logger.LogDebug(
                "Managed profile events unavailable for actor {ActorId}; status {StatusCode}. Using public events only.",
                actorId,
                ex.StatusCode);
            return publicEvents;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching managed profile events for actor {ActorId}. Using public events only.", actorId);
            return publicEvents;
        }
    }

    public async Task<ICollection<EventListDto>> GetPublicEventsByOrganizationAsync(Guid organizationId)
    {
        try
        {
            var result = await _apiClient.GetEventsAsync(
                pageNumber: 1,
                pageSize: 100,
                organizationId: organizationId,
                view: "All");
            return result?.GetItems() ?? new List<EventListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching public events for organization {OrganizationId}", organizationId);
            return new List<EventListDto>();
        }
    }

    public async Task<ICollection<EventListDto>> GetPublicEventsByGroupAsync(Guid groupId)
    {
        try
        {
            var result = await _apiClient.GetEventsAsync(
                pageNumber: 1,
                pageSize: 100,
                groupId: groupId,
                view: "All");
            return result?.GetItems() ?? new List<EventListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching public events for group {GroupId}", groupId);
            return new List<EventListDto>();
        }
    }

    private static ICollection<EventListDto> MergeProfileEvents(
        ICollection<EventListDto> publicEvents,
        ICollection<EventListDto> managedEvents)
    {
        if (managedEvents.Count == 0)
            return publicEvents;

        var remainingManagedEvents = managedEvents.ToList();
        var merged = new List<EventListDto>(publicEvents.Count + remainingManagedEvents.Count);
        foreach (var publicEvent in publicEvents)
        {
            if (publicEvent.Id is not { } publicEventId || publicEventId == Guid.Empty)
            {
                merged.Add(publicEvent);
                continue;
            }

            var managedIndex = remainingManagedEvents.FindIndex(managedEvent => managedEvent.Id == publicEventId);
            if (managedIndex >= 0)
            {
                var managedEvent = remainingManagedEvents[managedIndex];
                remainingManagedEvents.RemoveAt(managedIndex);
                merged.Add(managedEvent);
                continue;
            }

            merged.Add(publicEvent);
        }

        merged.AddRange(remainingManagedEvents);
        return merged;
    }
}
