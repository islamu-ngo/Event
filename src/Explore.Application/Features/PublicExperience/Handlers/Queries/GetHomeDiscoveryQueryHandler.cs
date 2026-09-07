using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.PublicExperience.Handlers.Queries;

public sealed partial class GetHomeDiscoveryQueryHandler(
    IRequestHandler<GetPublicEventDiscoveryRequest, PaginatedResult<EventDiscoveryItemDto>> eventDiscoveryHandler,
    IRequestHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto> shellHandler,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settingsResolver,
    ILocationRepository locationRepository,
    TimeProvider timeProvider,
    ILogger<GetHomeDiscoveryQueryHandler> logger)
    : IRequestHandler<GetHomeDiscoveryQuery, HomeDiscoveryDto>
{
    private const int HeroLimit = 10;
    private const int UpcomingLimit = 18;
    private const int StandardLimit = 10;
    private const int SpotlightLimit = 3;
    private const int MaximumCuratedSections = 2;
    private static readonly TimeSpan SectionTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CompositeTimeout = TimeSpan.FromSeconds(3);
    private static readonly int[] OnlineFormatIds =
        [(int)EventFormatEnum.Digital, (int)EventFormatEnum.Hybrid];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HomeDiscoveryDto> Handle(
        GetHomeDiscoveryQuery request,
        CancellationToken cancellationToken)
    {
        var generatedAt = timeProvider.GetUtcNow();
        var context = new HomeDiscoveryContextDto();
        IReadOnlyList<EventDiscoveryItemDto> hero = [];
        IReadOnlyList<EventDiscoveryItemDto> upcomingInArea = [];
        HomeDiscoverySectionDto? spotlight = null;
        IReadOnlyList<EventDiscoveryItemDto> mostViewedInArea = [];
        IReadOnlyList<EventDiscoveryItemDto> mostViewedOnline = [];
        IReadOnlyList<HomeDiscoverySectionDto> curatedSections = [];
        IReadOnlyList<EventDiscoveryItemDto> recentlyAdded = [];
        var sectionStatuses = new Dictionary<string, HomeDiscoverySectionStatus>(StringComparer.Ordinal);
        using var compositeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        compositeCancellation.CancelAfter(CompositeTimeout);
        var operationToken = compositeCancellation.Token;

        HomeDiscoveryDto Snapshot() => new()
        {
            Context = context,
            Hero = hero,
            UpcomingInArea = upcomingInArea,
            Spotlight = spotlight,
            MostViewedInArea = mostViewedInArea,
            MostViewedOnline = mostViewedOnline,
            CuratedSections = curatedSections,
            RecentlyAdded = recentlyAdded,
            SectionStatuses = sectionStatuses,
            GeneratedAtUtc = generatedAt
        };

        try
        {
            var today = DateOnly.FromDateTime(generatedAt.UtcDateTime);
            var areaState = await ResolveAreaStateAsync(request, operationToken);
            context = MapContext(areaState);
            var heroRequest = ApplyContext(
                CreateUpcomingRequest(today, "views", sortDescending: true, HeroLimit),
                areaState);
            if (heroRequest is not null)
            {
                hero = await QuerySectionAsync(
                    "hero", heroRequest, HeroLimit, sectionStatuses, operationToken);
            }
            else
            {
                sectionStatuses["hero"] = HomeDiscoverySectionStatus.Empty;
            }

            var upcomingRequest = ApplyContext(
                CreateUpcomingRequest(today, "date", sortDescending: false, UpcomingLimit),
                areaState);
            if (upcomingRequest is not null)
            {
                upcomingInArea = await QuerySectionAsync(
                    "upcoming", upcomingRequest, UpcomingLimit, sectionStatuses, operationToken);
            }
            else
            {
                sectionStatuses["upcoming"] = HomeDiscoverySectionStatus.Empty;
            }

            spotlight = await BuildSpotlightAsync(areaState, today, sectionStatuses, operationToken);

            if (areaState.Mode == HomeDiscoveryMode.Area &&
                areaState.SelectedArea?.LocationIds is { Count: > 0 } areaLocationIds)
            {
                var mostViewedAreaRequest = CreateUpcomingRequest(
                    today,
                    "views",
                    sortDescending: true,
                    StandardLimit) with
                {
                    LocationIds = areaLocationIds.Distinct().ToList(),
                    FormatIds = [(int)EventFormatEnum.Local, (int)EventFormatEnum.Hybrid]
                };
                mostViewedInArea = await QuerySectionAsync(
                    "most-viewed-area",
                    mostViewedAreaRequest,
                    StandardLimit,
                    sectionStatuses,
                    operationToken);
            }
            else
            {
                sectionStatuses["most-viewed-area"] = HomeDiscoverySectionStatus.Omitted;
            }

            var mostViewedOnlineRequest = CreateUpcomingRequest(
                today,
                "views",
                sortDescending: true,
                StandardLimit) with
            {
                FormatIds = [.. OnlineFormatIds]
            };
            mostViewedOnline = await QuerySectionAsync(
                "most-viewed-online",
                mostViewedOnlineRequest,
                StandardLimit,
                sectionStatuses,
                operationToken);

            curatedSections = await BuildCuratedSectionsAsync(
                areaState, today, sectionStatuses, operationToken);

            var recentlyAddedRequest = ApplyContext(
                CreateUpcomingRequest(today, "createdat", sortDescending: true, StandardLimit),
                areaState);
            if (recentlyAddedRequest is not null)
            {
                recentlyAdded = await QuerySectionAsync(
                    "recently-added",
                    recentlyAddedRequest,
                    StandardLimit,
                    sectionStatuses,
                    operationToken);
            }
            else
            {
                sectionStatuses["recently-added"] = HomeDiscoverySectionStatus.Empty;
            }

            return Snapshot();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            sectionStatuses["composite"] = HomeDiscoverySectionStatus.Failed;
            LogCompositeTimeout(logger);
            return Snapshot();
        }
    }

    private async Task<AreaState> ResolveAreaStateAsync(
        GetHomeDiscoveryQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var settingContext = new SettingContext(TenantId: tenantContext.TenantId);
            var rawConfig = await settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.PublicExperience.DiscoveryAreas,
                settingContext,
                cancellationToken);
            var config = Deserialize<PublicDiscoveryAreasConfig>(rawConfig) ?? new PublicDiscoveryAreasConfig();
            var configuredAreas = config.Areas ?? [];
            var referencedLocationIds = configuredAreas
                .SelectMany(area => area.LocationIds ?? [])
                .ToHashSet();
            var tenantLocationIds = referencedLocationIds.Count == 0
                ? new HashSet<Guid>()
                : (await locationRepository.GetExistingTenantLocationIdsAsync(
                        tenantContext.TenantId,
                        referencedLocationIds,
                        cancellationToken))
                    .ToHashSet();
            var validationErrors = PublicDiscoveryAreasConfigValidator.Validate(config, tenantLocationIds);
            if (validationErrors.Count > 0)
                return AreaState.Empty(ParseMode(request.Mode));

            var activeAreas = configuredAreas
                .Where(area => area.IsActive)
                .OrderBy(area => area.SortOrder)
                .ThenBy(area => area.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(area => area.Id)
                .ToList();
            var selectedArea = activeAreas.FirstOrDefault(area => area.Id == request.AreaId) ??
                               activeAreas.FirstOrDefault(area => area.IsDefault) ??
                               activeAreas.FirstOrDefault();
            var mode = ParseMode(request.Mode);
            if (mode == HomeDiscoveryMode.Area && selectedArea is null)
                mode = HomeDiscoveryMode.All;

            return new AreaState(mode, activeAreas, selectedArea);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSectionFailure(logger, exception, "context");
            return AreaState.Empty(ParseMode(request.Mode));
        }
    }

    private async Task<HomeDiscoverySectionDto?> BuildSpotlightAsync(
        AreaState areaState,
        DateOnly today,
        Dictionary<string, HomeDiscoverySectionStatus> statuses,
        CancellationToken cancellationToken)
    {
        var presets = await ResolveCuratedPresetsAsync(cancellationToken);
        var spotlightPreset = presets.FirstOrDefault(IsSpotlightPreset);
        GetEventListRequest? spotlightRequest = null;
        var label = "Community spotlight";

        if (spotlightPreset is not null && TryBuildCuratedRequest(spotlightPreset, today, out var curatedRequest))
        {
            spotlightRequest = curatedRequest;
            label = spotlightPreset.Label.Trim();
        }
        else
        {
            try
            {
                var shell = await shellHandler.Handle(new GetPublicExperienceShellQuery(), cancellationToken);
                if (shell.PrimaryOrganization.ActorId is { } actorId)
                {
                    spotlightRequest = CreateUpcomingRequest(
                        today,
                        "date",
                        sortDescending: false,
                        SpotlightLimit) with
                    {
                        ActorId = actorId
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogSectionFailure(logger, exception, "spotlight");
                statuses["spotlight"] = HomeDiscoverySectionStatus.Failed;
                return null;
            }
        }

        if (spotlightRequest is null)
        {
            statuses["spotlight"] = HomeDiscoverySectionStatus.Omitted;
            return null;
        }

        spotlightRequest = ApplyContext(spotlightRequest, areaState);
        if (spotlightRequest is null)
        {
            statuses["spotlight"] = HomeDiscoverySectionStatus.Empty;
            return new HomeDiscoverySectionDto { Key = "spotlight", Label = label };
        }

        var items = await QuerySectionAsync(
            "spotlight", spotlightRequest, SpotlightLimit, statuses, cancellationToken);
        return new HomeDiscoverySectionDto { Key = "spotlight", Label = label, Items = items };
    }

    private async Task<List<HomeDiscoverySectionDto>> BuildCuratedSectionsAsync(
        AreaState areaState,
        DateOnly today,
        Dictionary<string, HomeDiscoverySectionStatus> statuses,
        CancellationToken cancellationToken)
    {
        var sections = new List<HomeDiscoverySectionDto>();
        var presets = await ResolveCuratedPresetsAsync(cancellationToken);

        foreach (var preset in presets
                     .Where(preset => !IsSpotlightPreset(preset))
                     .Take(MaximumCuratedSections))
        {
            if (!TryBuildCuratedRequest(preset, today, out var request))
                continue;

            var limit = Math.Clamp(preset.Limit ?? StandardLimit, 1, StandardLimit);
            var key = $"curated:{preset.Id.Trim()}";
            var contextualRequest = ApplyContext(request, areaState);
            var items = contextualRequest is not null
                ? await QuerySectionAsync(key, contextualRequest, limit, statuses, cancellationToken)
                : [];
            if (!statuses.ContainsKey(key))
                statuses[key] = HomeDiscoverySectionStatus.Empty;
            sections.Add(new HomeDiscoverySectionDto
            {
                Key = preset.Id.Trim(),
                Label = preset.Label.Trim(),
                Items = items
            });
        }

        return sections;
    }

    private async Task<IReadOnlyList<PublicEventSectionPresetConfig>> ResolveCuratedPresetsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var rawConfig = await settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.PublicExperience.EventSectionPresets,
                new SettingContext(TenantId: tenantContext.TenantId),
                cancellationToken);
            var config = Deserialize<PublicEventSectionPresetsConfig>(rawConfig);
            return config?.Presets?
                .Where(preset => preset.IsEnabled &&
                                 !string.IsNullOrWhiteSpace(preset.Id) &&
                                 !string.IsNullOrWhiteSpace(preset.Label))
                .OrderBy(preset => preset.SortOrder)
                .ThenBy(preset => preset.Id, StringComparer.OrdinalIgnoreCase)
                .GroupBy(preset => preset.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList() ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSectionFailure(logger, exception, "curated-config");
            return [];
        }
    }

    private async Task<List<EventDiscoveryItemDto>> QuerySectionAsync(
        string key,
        GetEventListRequest request,
        int limit,
        Dictionary<string, HomeDiscoverySectionStatus> statuses,
        CancellationToken cancellationToken)
    {
        request = request with { PageNumber = 1, PageSize = limit };

        try
        {
            using var sectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sectionCancellation.CancelAfter(SectionTimeout);
            var page = await eventDiscoveryHandler.Handle(
                new GetPublicEventDiscoveryRequest(request),
                sectionCancellation.Token);
            var items = page.Items
                .Where(item =>
                    (item.Event is { Id: var localId } && localId != Guid.Empty)
                    || (item.FederatedEvent is { Id: var federatedId } && federatedId != Guid.Empty))
                .Take(limit)
                .Select(MapDiscoveryItem)
                .ToList();
            statuses[key] = items.Count > 0
                ? HomeDiscoverySectionStatus.Available
                : HomeDiscoverySectionStatus.Empty;
            return items;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            LogSectionTimeout(logger, key);
            statuses[key] = HomeDiscoverySectionStatus.Failed;
            return [];
        }
        catch (Exception exception)
        {
            LogSectionFailure(logger, exception, key);
            statuses[key] = HomeDiscoverySectionStatus.Failed;
            return [];
        }
    }

    private static HomeDiscoveryContextDto MapContext(AreaState state)
    {
        var defaultArea = state.ActiveAreas.FirstOrDefault(area => area.IsDefault);
        return new HomeDiscoveryContextDto
        {
            Mode = state.Mode,
            SelectedAreaId = state.SelectedArea?.Id,
            DefaultAreaId = defaultArea?.Id,
            SelectedAreaDisplayName = state.SelectedArea?.DisplayName.Trim() ?? string.Empty,
            AvailableAreas = state.ActiveAreas.Select(MapArea).ToList()
        };
    }

    private static EventListDto MapEvent(EventListDto source) => new()
    {
        Id = source.Id,
        Title = Bound(source.Title, 240),
        Subtitle = BoundNullable(source.Subtitle, 160),
        Description = BoundNullable(source.Description, 150),
        Slug = BoundNullable(source.Slug, 180),
        PublicCode = BoundNullable(source.PublicCode, 12),
        EventTypeId = source.EventTypeId,
        EventTypeFullName = Bound(source.EventTypeFullName, 80),
        AudienceGenderId = source.AudienceGenderId,
        AudienceGenderFullName = Bound(source.AudienceGenderFullName, 80),
        AudienceAgeId = source.AudienceAgeId,
        AudienceAgeFullName = Bound(source.AudienceAgeFullName, 80),
        ActorId = source.ActorId,
        ActorDisplayName = Bound(source.ActorDisplayName, 160),
        ActorTypeId = source.ActorTypeId,
        ActorTypeFullName = Bound(source.ActorTypeFullName, 80),
        TicketPriceSummary = source.TicketPriceSummary,
        FeaturedImageId = source.FeaturedImageId,
        FeaturedImageUri = BoundNullable(source.FeaturedImageUri, 500),
        EventStatusId = source.EventStatusId,
        EventStatusFullName = Bound(source.EventStatusFullName, 80),
        VisibilityTypeId = source.VisibilityTypeId,
        VisibilityTypeFullName = Bound(source.VisibilityTypeFullName, 80),
        EventFormatId = source.EventFormatId,
        EventFormatFullName = Bound(source.EventFormatFullName, 80),
        FirstSessionDate = source.FirstSessionDate,
        FirstSessionStartUtc = source.FirstSessionStartUtc,
        IsPast = source.IsPast,
        CreatedAtUtc = source.CreatedAtUtc,
        AtprotoRecordId = source.AtprotoRecordId,
        TenantId = source.TenantId
    };

    private static EventDiscoveryItemDto MapDiscoveryItem(EventDiscoveryItemDto source) => new()
    {
        Source = source.Source,
        Event = source.Event is null ? null : MapEvent(source.Event),
        FederatedEvent = source.FederatedEvent,
        Federation = source.Federation
    };

    private static string Bound(string value, int maximumLength) =>
        BoundNullable(value, maximumLength) ?? string.Empty;

    private static string? BoundNullable(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
            return value;

        var length = maximumLength;
        if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            length--;

        return value[..length].TrimEnd();
    }

    private static PublicDiscoveryAreaDto MapArea(PublicDiscoveryAreaConfig area) => new()
    {
        Id = area.Id,
        DisplayName = area.DisplayName.Trim(),
        City = area.City.Trim(),
        CountryCode = area.CountryCode.Trim().ToUpperInvariant(),
        CentroidLatitude = area.CentroidLatitude,
        CentroidLongitude = area.CentroidLongitude,
        IsDefault = area.IsDefault,
        SortOrder = area.SortOrder
    };

    private static GetEventListRequest? ApplyContext(GetEventListRequest request, AreaState state)
    {
        if (state.Mode == HomeDiscoveryMode.Online)
        {
            var formatIds = request.FormatIds is { Count: > 0 }
                ? request.FormatIds.Intersect(OnlineFormatIds).ToList()
                : [.. OnlineFormatIds];
            return formatIds.Count > 0
                ? request with { FormatIds = formatIds, LocationIds = null }
                : null;
        }

        if (state.Mode == HomeDiscoveryMode.Area)
        {
            if (state.SelectedArea?.LocationIds is not { Count: > 0 } locationIds)
                return null;

            return request with { LocationIds = locationIds.Distinct().ToList() };
        }

        return request;
    }

    private static GetEventListRequest CreateUpcomingRequest(
        DateOnly today,
        string sortBy,
        bool sortDescending,
        int limit) =>
        new()
        {
            PageNumber = 1,
            PageSize = limit,
            DateFrom = today,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

    private static bool TryBuildCuratedRequest(
        PublicEventSectionPresetConfig preset,
        DateOnly today,
        out GetEventListRequest request)
    {
        request = CreateUpcomingRequest(today, "date", sortDescending: false, preset.Limit ?? StandardLimit);
        var ownerIds = new[]
        {
            preset.Owners?.ActorIds?.Count ?? 0,
            preset.Owners?.OrganizationIds?.Count ?? 0,
            preset.Owners?.GroupIds?.Count ?? 0
        }.Sum();
        if (ownerIds > 1 || preset.Filters?.CustomProperties is { Count: > 0 })
            return false;

        request = request with
        {
            ActorId = preset.Owners?.ActorIds?.SingleOrDefault(),
            OrganizationId = preset.Owners?.OrganizationIds?.SingleOrDefault(),
            GroupId = preset.Owners?.GroupIds?.SingleOrDefault(),
            IncludedCategoryIds = preset.Filters?.CategoryIds?.Distinct().ToList(),
            IncludedTagIds = preset.Filters?.TagIds?.Distinct().ToList(),
            AudienceGenderIds = preset.Filters?.AudienceGenderIds?.Distinct().ToList(),
            AudienceAgeIds = preset.Filters?.AudienceAgeIds?.Distinct().ToList(),
            EventTypeIds = preset.Filters?.EventTypeIds?.Distinct().ToList(),
            FormatIds = preset.Filters?.EventFormatIds?.Distinct().ToList()
        };

        if (preset.Filters?.Date is { } dateFilter)
        {
            if (dateFilter.Window == PublicEventSectionDateWindow.Past)
                return false;

            if (dateFilter.Window == PublicEventSectionDateWindow.Custom)
            {
                if (!dateFilter.StartsOnOrAfter.HasValue && !dateFilter.StartsOnOrBefore.HasValue)
                    return false;

                request = request with
                {
                    DateFrom = dateFilter.StartsOnOrAfter,
                    DateTo = dateFilter.StartsOnOrBefore
                };
            }
        }

        return true;
    }

    private static bool IsSpotlightPreset(PublicEventSectionPresetConfig preset) =>
        string.Equals(preset.Id.Trim(), "spotlight", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(preset.Id.Trim(), "community-spotlight", StringComparison.OrdinalIgnoreCase);

    private static HomeDiscoveryMode ParseMode(string? mode) =>
        mode?.Trim().ToLowerInvariant() switch
        {
            "area" => HomeDiscoveryMode.Area,
            "online" => HomeDiscoveryMode.Online,
            _ => HomeDiscoveryMode.All
        };

    private static T? Deserialize<T>(string? rawConfig)
    {
        if (string.IsNullOrWhiteSpace(rawConfig))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(rawConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Home discovery section {SectionKey} failed.")]
    private static partial void LogSectionFailure(
        ILogger logger,
        Exception exception,
        string sectionKey);

    [LoggerMessage(LogLevel.Warning, "Home discovery section {SectionKey} exceeded its time limit.")]
    private static partial void LogSectionTimeout(ILogger logger, string sectionKey);

    [LoggerMessage(LogLevel.Warning, "Home discovery composition exceeded its time limit.")]
    private static partial void LogCompositeTimeout(ILogger logger);

    private sealed record AreaState(
        HomeDiscoveryMode Mode,
        IReadOnlyList<PublicDiscoveryAreaConfig> ActiveAreas,
        PublicDiscoveryAreaConfig? SelectedArea)
    {
        public static AreaState Empty(HomeDiscoveryMode mode) =>
            new(mode == HomeDiscoveryMode.Area ? HomeDiscoveryMode.All : mode, [], null);
    }
}
