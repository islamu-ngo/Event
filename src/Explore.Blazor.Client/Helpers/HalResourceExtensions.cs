using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Models.CustomProperties;

namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Extension methods for HAL collection resources to provide typed Items accessors.
/// These extensions bridge generated HAL resource wrappers to the plain DTO/model shapes used by the UI.
/// </summary>
public static class HalResourceExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ========== Event Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of EventListDto.
    /// </summary>
    public static ICollection<EventListDto> GetItems(this HalCollectionResourceOfEventListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<EventListDto>();

        return DeserializeItems<EventListDto>(collection._embedded.Items);
    }

    public static ICollection<EventTeamMemberDto> GetItems(this HalCollectionResourceOfEventTeamMemberDto collection)
    {
        if (collection._embedded?.Items == null)
            return [];

        return DeserializeItems<EventTeamMemberDto>(collection._embedded.Items);
    }

    public static ICollection<EventListDto> GetItems(this HalCollectionResourceOfEventDiscoveryItemDto collection)
    {
        if (collection._embedded?.Items is not { Count: > 0 } items)
            return [];

        return items
            .Select(ToEventListDto)
            .OfType<EventListDto>()
            .ToList();
    }

    public static EventListDto? ToEventListDto(this EventDiscoveryItemDto item) =>
        MapDiscoveryItem(
            item.Source,
            item.Event,
            item.FederatedEvent,
            item.Federation,
            item.AdditionalProperties.TryGetValue("_links", out var links) ? links : null);

    public static EventListDto? ToEventListDto(this HalResourceOfEventDiscoveryItemDto item) =>
        MapDiscoveryItem(
            item.Source,
            item.Event,
            item.FederatedEvent,
            item.Federation,
            item._links is { Count: > 0 }
                ? JsonSerializer.SerializeToElement(item._links, JsonOptions)
                : null);

    /// <summary>
    /// Converts HalResourceOfEventDto to EventDto using JSON serialization.
    /// This ensures all properties are correctly mapped regardless of changes in the DTO schema.
    /// </summary>
    public static EventDto ToDto(this HalResourceOfEventDto halResource)
    {
        // Use JSON serialization to copy all matching properties
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<EventDto>(json, JsonOptions) ?? new EventDto();
    }

    public static ICollection<EventSeriesListDto> GetItems(this HalCollectionResourceOfEventSeriesListDto collection)
    {
        if (collection._embedded?.Items == null)
            return [];

        return DeserializeItems<EventSeriesListDto>(collection._embedded.Items);
    }

    public static EventSeriesDto ToDto(this HalResourceOfEventSeriesDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<EventSeriesDto>(json, JsonOptions) ?? new EventSeriesDto();
    }

    public static PaginatedResult<EventSeriesListDto> ToPaginatedResult(
        this HalCollectionResourceOfEventSeriesListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<EventSeriesListDto>.Empty();

        return new PaginatedResult<EventSeriesListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0,
            Links = ToClientLinks(collection._links)
        };
    }

    // ========== EventSession Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of EventSessionListDto.
    /// </summary>
    public static ICollection<EventSessionListDto> GetItems(this HalCollectionResourceOfEventSessionListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<EventSessionListDto>();

        return DeserializeItems<EventSessionListDto>(collection._embedded.Items);
    }

    public static ICollection<EventSessionSpeakerListDto> GetItems(this HalCollectionResourceOfEventSessionSpeakerListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<EventSessionSpeakerListDto>();

        return DeserializeItems<EventSessionSpeakerListDto>(collection._embedded.Items);
    }

    /// <summary>
    /// Converts HalResourceOfEventSessionDto to EventSessionDto using JSON serialization.
    /// </summary>
    public static EventSessionDto ToDto(this HalResourceOfEventSessionDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<EventSessionDto>(json, JsonOptions) ?? new EventSessionDto();
    }

    public static ICollection<EventSessionLanguageListDto> GetItems(
        this HalCollectionResourceOfEventSessionLanguageListDto collection)
    {
        if (collection._embedded?.Items == null)
            return [];

        return DeserializeItems<EventSessionLanguageListDto>(collection._embedded.Items);
    }

    public static UserDto ToDto(this HalResourceOfUserDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<UserDto>(json, JsonOptions) ?? new UserDto();
    }

    // ========== EventSessionGroup Extensions ==========

    /// <summary>
    /// Gets typed event program section/track/devroom items from a HAL collection.
    /// </summary>
    public static ICollection<HalResourceOfEventSessionGroupListDto> GetItems(this HalCollectionResourceOfEventSessionGroupListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<HalResourceOfEventSessionGroupListDto>();

        return collection._embedded.Items.ToList();
    }

    // ========== Category Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of CategoryListDto.
    /// </summary>
    public static ICollection<CategoryListDto> GetItems(this HalCollectionResourceOfCategoryListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<CategoryListDto>();

        return DeserializeItems<CategoryListDto>(collection._embedded.Items);
    }

    /// <summary>
    /// Converts HalResourceOfCategoryDto to CategoryDto using JSON serialization.
    /// </summary>
    public static CategoryDto ToDto(this HalResourceOfCategoryDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<CategoryDto>(json, JsonOptions) ?? new CategoryDto();
    }

    // ========== Tag Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of TagListDto.
    /// </summary>
    public static ICollection<TagListDto> GetItems(this HalCollectionResourceOfTagListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<TagListDto>();

        return DeserializeItems<TagListDto>(collection._embedded.Items);
    }

    /// <summary>
    /// Converts HalResourceOfTagDto to TagDto using JSON serialization.
    /// </summary>
    public static TagDto ToDto(this HalResourceOfTagDto halResource)
    {
        try
        {
            var json = JsonSerializer.Serialize(halResource, JsonOptions);
            return JsonSerializer.Deserialize<TagDto>(json, JsonOptions) ?? new TagDto();
        }
        catch (JsonException)
        {
            return new TagDto
            {
                Id = halResource.Id,
                FullName = halResource.FullName ?? string.Empty,
                MasterCode = halResource.MasterCode ?? string.Empty
            };
        }
    }

    // ========== Location Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of LocationListDto.
    /// </summary>
    public static ICollection<LocationListDto> GetItems(this HalCollectionResourceOfLocationListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<LocationListDto>();

        return DeserializeItems<LocationListDto>(collection._embedded.Items);
    }

    /// <summary>
    /// Converts HalResourceOfLocationDto to LocationDto using JSON serialization.
    /// </summary>
    public static LocationDto ToDto(this HalResourceOfLocationDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<LocationDto>(json, JsonOptions) ?? new LocationDto();
    }

    // ========== EventLocation Extensions ==========

    /// <summary>
    /// Materializes review-queue rows as HAL resources rather than bare DTOs. Each EventLocation carries
    /// its own <c>_links</c>, and a manager may be able to remediate one row but not another in the same
    /// queue — collapsing to the DTO would discard exactly the data the table needs.
    /// </summary>
    public static IReadOnlyList<HalResourceOfEventLocationManagementDto> GetItems(
        this HalCollectionResourceOfEventLocationManagementDto? collection)
    {
        if (collection?._embedded?.Items is not { Count: > 0 } items)
            return [];

        return [.. items];
    }

    public static bool HasLink(this HalResourceOfEventLocationManagementDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasLink(this HalCollectionResourceOfEventLocationManagementDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    // ========== Organization Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of OrganizationListDto.
    /// </summary>
    public static ICollection<OrganizationListDto> GetItems(this HalCollectionResourceOfOrganizationListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<OrganizationListDto>();

        return DeserializeItems<OrganizationListDto>(collection._embedded.Items);
    }

    /// <summary>
    /// Converts HalResourceOfOrganizationDto to OrganizationDto using JSON serialization.
    /// </summary>
    public static OrganizationDto ToDto(this HalResourceOfOrganizationDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<OrganizationDto>(json, JsonOptions) ?? new OrganizationDto();
    }

    public static ICollection<OrganizationTenantEvidenceDto> GetItems(
        this HalCollectionResourceOfOrganizationTenantEvidenceDto collection)
    {
        if (collection._embedded?.Items == null)
            return [];

        return DeserializeItems<OrganizationTenantEvidenceDto>(collection._embedded.Items);
    }

    // ========== Actor Extensions ==========

    /// <summary>
    /// Gets the typed items from a HAL collection of ActorListDto.
    /// </summary>
    public static ICollection<ActorListDto> GetItems(this HalCollectionResourceOfActorListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<ActorListDto>();

        return DeserializeItems<ActorListDto>(collection._embedded.Items);
    }

    /// <summary>
    /// Converts HalResourceOfActorDto to ActorDto using JSON serialization.
    /// </summary>
    public static ActorDto ToDto(this HalResourceOfActorDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<ActorDto>(json, JsonOptions) ?? new ActorDto();
    }

    // ========== Actor Subscription Extensions ==========

    public static ICollection<ActorSubscriptionListDto> GetItems(this HalCollectionResourceOfActorSubscriptionListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<ActorSubscriptionListDto>();

        return DeserializeItems<ActorSubscriptionListDto>(collection._embedded.Items);
    }

    public static ActorSubscriptionDto ToDto(this HalResourceOfActorSubscriptionDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<ActorSubscriptionDto>(json, JsonOptions) ?? new ActorSubscriptionDto();
    }

    public static ICollection<HalResourceOfMyEventReportDto> GetItems(this HalCollectionResourceOfMyEventReportDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<HalResourceOfMyEventReportDto>();

        return collection._embedded.Items.ToList();
    }

    public static ICollection<HalResourceOfModerationReportQueueItemDto> GetItems(this HalCollectionResourceOfModerationReportQueueItemDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<HalResourceOfModerationReportQueueItemDto>();

        return collection._embedded.Items.ToList();
    }

    // ========== LINQ-like Extensions for HAL Collections ==========

    /// <summary>
    /// Checks if the HAL collection contains any items.
    /// </summary>
    public static bool Any(this HalCollectionResourceOfEventListDto collection)
        => collection._embedded?.Items?.Count > 0;

    public static bool Any(this HalCollectionResourceOfEventSessionListDto collection)
        => collection._embedded?.Items?.Count > 0;

    public static bool Any(this HalCollectionResourceOfCategoryListDto collection)
        => collection._embedded?.Items?.Count > 0;

    public static bool Any(this HalCollectionResourceOfTagListDto collection)
        => collection._embedded?.Items?.Count > 0;

    public static bool Any(this HalCollectionResourceOfLocationListDto collection)
        => collection._embedded?.Items?.Count > 0;

    public static bool Any(this HalCollectionResourceOfOrganizationListDto collection)
        => collection._embedded?.Items?.Count > 0;

    public static bool Any(this HalCollectionResourceOfActorListDto collection)
        => collection._embedded?.Items?.Count > 0;

    /// <summary>
    /// Gets the count of items in the HAL collection.
    /// </summary>
    public static int Count(this HalCollectionResourceOfEventListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    public static int Count(this HalCollectionResourceOfEventSessionListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    public static int Count(this HalCollectionResourceOfCategoryListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    public static int Count(this HalCollectionResourceOfTagListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    public static int Count(this HalCollectionResourceOfLocationListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    public static int Count(this HalCollectionResourceOfOrganizationListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    public static int Count(this HalCollectionResourceOfActorListDto collection)
        => collection._embedded?.Items?.Count ?? 0;

    /// <summary>
    /// Converts HAL collection to a List for LINQ operations.
    /// </summary>
    public static List<EventListDto> ToList(this HalCollectionResourceOfEventListDto collection)
        => collection.GetItems().ToList();

    public static List<EventSessionListDto> ToList(this HalCollectionResourceOfEventSessionListDto collection)
        => collection.GetItems().ToList();

    public static List<CategoryListDto> ToList(this HalCollectionResourceOfCategoryListDto collection)
        => collection.GetItems().ToList();

    public static List<TagListDto> ToList(this HalCollectionResourceOfTagListDto collection)
        => collection.GetItems().ToList();

    public static List<LocationListDto> ToList(this HalCollectionResourceOfLocationListDto collection)
        => collection.GetItems().ToList();

    public static List<OrganizationListDto> ToList(this HalCollectionResourceOfOrganizationListDto collection)
        => collection.GetItems().ToList();

    public static List<ActorListDto> ToList(this HalCollectionResourceOfActorListDto collection)
        => collection.GetItems().ToList();

    /// <summary>
    /// Gets the first item or default from a HAL collection.
    /// </summary>
    public static EventListDto? FirstOrDefault(this HalCollectionResourceOfEventListDto collection)
        => collection.GetItems().FirstOrDefault();

    public static EventSessionListDto? FirstOrDefault(this HalCollectionResourceOfEventSessionListDto collection)
        => collection.GetItems().FirstOrDefault();

    public static CategoryListDto? FirstOrDefault(this HalCollectionResourceOfCategoryListDto collection)
        => collection.GetItems().FirstOrDefault();

    public static TagListDto? FirstOrDefault(this HalCollectionResourceOfTagListDto collection)
        => collection.GetItems().FirstOrDefault();

    public static LocationListDto? FirstOrDefault(this HalCollectionResourceOfLocationListDto collection)
        => collection.GetItems().FirstOrDefault();

    public static OrganizationListDto? FirstOrDefault(this HalCollectionResourceOfOrganizationListDto collection)
        => collection.GetItems().FirstOrDefault();

    public static ActorListDto? FirstOrDefault(this HalCollectionResourceOfActorListDto collection)
        => collection.GetItems().FirstOrDefault();

    /// <summary>
    /// Orders HAL collection items by a key selector.
    /// </summary>
    public static IOrderedEnumerable<EventSessionListDto> OrderBy<TKey>(
        this HalCollectionResourceOfEventSessionListDto collection,
        Func<EventSessionListDto, TKey> keySelector)
        => collection.GetItems().OrderBy(keySelector);

    /// <summary>
    /// Selects a projection from HAL collection items.
    /// </summary>
    public static IEnumerable<TResult> Select<TResult>(
        this HalCollectionResourceOfEventSessionListDto collection,
        Func<EventSessionListDto, TResult> selector)
        => collection.GetItems().Select(selector);

    // ========== Paginated Result Mappers ==========

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for Event lists.
    /// </summary>
    public static PaginatedResult<EventListDto> ToPaginatedResult(this HalCollectionResourceOfEventListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<EventListDto>.Empty();

        return new PaginatedResult<EventListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0,
            Links = ToClientLinks(collection._links)
        };
    }

    public static PaginatedResult<EventListDto> ToPaginatedResult(this HalCollectionResourceOfEventDiscoveryItemDto? collection)
    {
        if (collection is null)
            return PaginatedResult<EventListDto>.Empty();

        return new PaginatedResult<EventListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for EventSession lists.
    /// </summary>
    public static PaginatedResult<EventSessionListDto> ToPaginatedResult(this HalCollectionResourceOfEventSessionListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<EventSessionListDto>.Empty();

        return new PaginatedResult<EventSessionListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for Category lists.
    /// </summary>
    public static PaginatedResult<CategoryListDto> ToPaginatedResult(this HalCollectionResourceOfCategoryListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<CategoryListDto>.Empty();

        return new PaginatedResult<CategoryListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for Tag lists.
    /// </summary>
    public static PaginatedResult<TagListDto> ToPaginatedResult(this HalCollectionResourceOfTagListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<TagListDto>.Empty();

        return new PaginatedResult<TagListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for Location lists.
    /// </summary>
    public static PaginatedResult<LocationListDto> ToPaginatedResult(this HalCollectionResourceOfLocationListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<LocationListDto>.Empty();

        return new PaginatedResult<LocationListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for Organization lists.
    /// </summary>
    public static PaginatedResult<OrganizationListDto> ToPaginatedResult(this HalCollectionResourceOfOrganizationListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<OrganizationListDto>.Empty();

        return new PaginatedResult<OrganizationListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    /// <summary>
    /// Maps a HAL collection response to a <see cref="PaginatedResult{T}"/> for Actor lists.
    /// </summary>
    public static PaginatedResult<ActorListDto> ToPaginatedResult(this HalCollectionResourceOfActorListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<ActorListDto>.Empty();

        return new PaginatedResult<ActorListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    // ========== EventDay Extensions ==========

    public static ICollection<EventDayListDto> GetItems(this HalCollectionResourceOfEventDayListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<EventDayListDto>();

        return DeserializeItems<EventDayListDto>(collection._embedded.Items);
    }

    public static EventDayDto ToDto(this HalResourceOfEventDayDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<EventDayDto>(json, JsonOptions) ?? new EventDayDto();
    }

    // ========== EventAgendaItem Extensions ==========

    public static ICollection<EventAgendaItemListDto> GetItems(this HalCollectionResourceOfEventAgendaItemListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<EventAgendaItemListDto>();

        return DeserializeItems<EventAgendaItemListDto>(collection._embedded.Items);
    }

    public static EventAgendaItemDto ToDto(this HalResourceOfEventAgendaItemDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<EventAgendaItemDto>(json, JsonOptions) ?? new EventAgendaItemDto();
    }

    // ========== LocationRoom Extensions ==========

    public static ICollection<LocationRoomListDto> GetItems(this HalCollectionResourceOfLocationRoomListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<LocationRoomListDto>();

        return DeserializeItems<LocationRoomListDto>(collection._embedded.Items);
    }

    public static LocationRoomDto ToDto(this HalResourceOfLocationRoomDto halResource)
    {
        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<LocationRoomDto>(json, JsonOptions) ?? new LocationRoomDto();
    }

    // ========== Custom Property Definition Extensions ==========

    public static ICollection<CustomPropertyDefinitionListDto> GetItems(this HalCollectionResourceOfCustomPropertyDefinitionListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<CustomPropertyDefinitionListDto>();

        return DeserializeItems<CustomPropertyDefinitionListDto>(collection._embedded.Items);
    }

    public static PaginatedResult<CustomPropertyDefinitionListDto> ToPaginatedResult(this HalCollectionResourceOfCustomPropertyDefinitionListDto? collection)
    {
        if (collection is null)
            return PaginatedResult<CustomPropertyDefinitionListDto>.Empty();

        return new PaginatedResult<CustomPropertyDefinitionListDto>
        {
            Items = collection.GetItems().ToList(),
            PageNumber = collection.PageNumber ?? 1,
            PageSize = collection.PageSize ?? 20,
            TotalCount = collection.TotalCount ?? 0
        };
    }

    public static ICollection<CustomPropertyDefinitionListDto> GetItems(this HalCollectionResourceOfEventCustomPropertyDefinitionListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<CustomPropertyDefinitionListDto>();

        return DeserializeItems<CustomPropertyDefinitionListDto>(collection._embedded.Items);
    }

    public static ICollection<CustomPropertyDefinitionListDto> GetItems(this HalCollectionResourceOfEventSessionCustomPropertyDefinitionListDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<CustomPropertyDefinitionListDto>();

        return DeserializeItems<CustomPropertyDefinitionListDto>(collection._embedded.Items);
    }

    public static CustomPropertyDefinitionDto? ToDto(this HalResourceOfCustomPropertyDefinitionDto? halResource)
    {
        if (halResource is null)
            return null;

        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<CustomPropertyDefinitionDto>(json, JsonOptions);
    }

    public static CustomPropertyDefinitionDto? ToDto(this HalResourceOfEventCustomPropertyDefinitionDto? halResource)
    {
        if (halResource is null)
            return null;

        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<CustomPropertyDefinitionDto>(json, JsonOptions);
    }

    public static CustomPropertyDefinitionDto? ToDto(this HalResourceOfEventSessionCustomPropertyDefinitionDto? halResource)
    {
        if (halResource is null)
            return null;

        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<CustomPropertyDefinitionDto>(json, JsonOptions);
    }

    public static InstanceOnboardingStatusDto? ToDto(this HalResourceOfInstanceOnboardingStatusDto? halResource)
    {
        if (halResource is null)
            return null;

        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<InstanceOnboardingStatusDto>(json, JsonOptions);
    }

    public static TenantOnboardingStatusDto? ToDto(this HalResourceOfTenantOnboardingStatusDto? halResource)
    {
        if (halResource is null)
            return null;

        var json = JsonSerializer.Serialize(halResource, JsonOptions);
        return JsonSerializer.Deserialize<TenantOnboardingStatusDto>(json, JsonOptions);
    }

    // ========== HAL Link Helpers ==========

    public static bool HasHalLink(this EventListDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool IsFederatedDiscoveryEvent(this EventListDto dto) =>
        dto.AdditionalProperties.TryGetValue("eventDiscoverySource", out var source) &&
        string.Equals(source as string, "atproto", StringComparison.OrdinalIgnoreCase);

    public static string? GetHalHref(this EventListDto dto, string linkRel)
        => GetSafeHalHref(dto.AdditionalProperties, linkRel);

    public static string? GetHalHref(this EventDto dto, string linkRel)
        => GetSafeHalHref(dto.AdditionalProperties, linkRel);

    public static string? GetHalTitle(this EventListDto dto, string linkRel)
        => GetHalTitle(dto.AdditionalProperties, linkRel);

    public static string? GetHalTitle(this EventDto dto, string linkRel)
        => GetHalTitle(dto.AdditionalProperties, linkRel);

    public static string? GetHalHref(this HalResourceOfEventPublicActionDto dto, string linkRel)
        => dto._links?.TryGetValue(linkRel, out var link) == true
            ? GetSafeHalHref(link.Href)
            : null;

    private static string? GetSafeHalHref(IDictionary<string, object> additionalProperties, string linkRel)
    {
        if (!additionalProperties.TryGetValue("_links", out var links) ||
            links is not JsonElement { ValueKind: JsonValueKind.Object } linksElement ||
            !linksElement.TryGetProperty(linkRel, out var link) ||
            link.ValueKind != JsonValueKind.Object ||
            !link.TryGetProperty("href", out var href) ||
            href.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return GetSafeHalHref(href.GetString());
    }

    private static string? GetHalTitle(IDictionary<string, object> additionalProperties, string linkRel)
    {
        if (!additionalProperties.TryGetValue("_links", out var links) ||
            links is not JsonElement { ValueKind: JsonValueKind.Object } linksElement ||
            !linksElement.TryGetProperty(linkRel, out var link) ||
            link.ValueKind != JsonValueKind.Object ||
            !link.TryGetProperty("title", out var title) ||
            title.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(title.GetString()) ? null : title.GetString();
    }

    private static string? GetSafeHalHref(string? value)
    {
        if (value is not { Length: > 0 } ||
            value[0] != '/' ||
            (value.Length > 1 && value[1] == '/') ||
            value.Contains('\\') ||
            value.Any(char.IsControl))
        {
            return null;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' &&
                (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])))
            {
                return null;
            }
        }

        var queryStart = value.IndexOf('?');
        if (queryStart < 0)
        {
            return value;
        }

        var fragmentStart = value.IndexOf('#', queryStart + 1);
        var query = value[(queryStart + 1)..(fragmentStart < 0 ? value.Length : fragmentStart)];
        foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = parameter.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? parameter : parameter[..separator]);
            if (key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("assertion", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("api_key", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("apikey", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return value;
    }

    public static bool HasHalLink(this InstanceOnboardingStatusDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this TenantOnboardingStatusDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasManagementLinks(this EventListDto dto)
        => dto.HasHalLink("edit") || dto.HasHalLink("delete");

    public static bool HasLink(this HalResourceOfEventDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    // ========== Scheduler Administration Extensions ==========

    public static bool HasLink(this HalResourceOfSchedulerAdminOverviewDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    /// <summary>
    /// Affordance check for one scheduled job. Each row carries its own link map, so a paused job and a running
    /// job in the same table legitimately answer differently for the same relation.
    /// </summary>
    public static bool HasLink(this HalResourceOfSchedulerAdminJobDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    /// <summary>
    /// Materializes embedded job rows as HAL resources rather than bare DTOs. The generator types the collection
    /// as <c>object</c>, and deserializing to the plain DTO here would discard each row's <c>_links</c> — which is
    /// precisely the data the table needs to decide which per-job controls to render.
    /// </summary>
    public static IReadOnlyList<HalResourceOfSchedulerAdminJobDto> SchedulerJobs(
        this HalCollectionResourceOfSchedulerAdminJobDto? resource)
    {
        if (resource?._embedded?.Items is not { } items)
        {
            return [];
        }

        return [.. DeserializeItems<HalResourceOfSchedulerAdminJobDto>(items)];
    }

    public static bool HasHalLink(this HalResourceOfGroupDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasLink(this HalResourceOfNotificationPreferenceMatrixDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasLink(this HalResourceOfWebPushSubscriptionDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasHalLink(this EventDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this EventSessionDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this EventSessionListDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasLink(this HalCollectionResourceOfEventSessionSpeakerListDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasHalLink(this EventSessionSpeakerListDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this CustomPropertyDefinitionDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this HalResourceOfEventSessionGroupListDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    // ========== Organization HAL Link Helpers ==========

    public static bool HasHalLink(this OrganizationDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this OrganizationListDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this OrganizationTenantEvidenceDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static string? GetHalHref(this OrganizationTenantEvidenceDto dto, string linkRel)
        => GetSafeHalHref(dto.AdditionalProperties, linkRel);

    public static bool HasManagementLinks(this OrganizationListDto dto)
        => dto.HasHalLink("edit") || dto.HasHalLink("delete");

    public static bool HasLink(this HalResourceOfOrganizationDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static ICollection<OrganizationMemberDto> GetItems(this HalCollectionResourceOfOrganizationMemberDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<OrganizationMemberDto>();

        return DeserializeItems<OrganizationMemberDto>(collection._embedded.Items);
    }

    public static bool HasLink(this HalCollectionResourceOfOrganizationMemberDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasHalLink(this OrganizationMemberDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    // ========== Group HAL Link Helpers ==========

    public static ICollection<GroupMemberDto> GetItems(this HalCollectionResourceOfGroupMemberDto collection)
    {
        if (collection._embedded?.Items == null)
            return new List<GroupMemberDto>();

        return DeserializeItems<GroupMemberDto>(collection._embedded.Items);
    }

    public static bool HasLink(this HalCollectionResourceOfGroupMemberDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasHalLink(this GroupMemberDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasLink(this HalResourceOfGroupDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasManagementLinks(this HalResourceOfGroupDto dto)
        => dto.HasLink("edit") || dto.HasLink("delete");

    public static bool HasHalLink(this ActorSubscriptionDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this ActorSubscriptionListDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this ActorDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this ActorListDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasHalLink(this EventTeamMemberDto dto, string linkRel)
        => HasHalLinkInAdditionalProperties(dto.AdditionalProperties, linkRel);

    public static bool HasLink(this HalResourceOfActorSubscriptionDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasLink(this HalResourceOfMyEventReportDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasLink(this HalResourceOfModerationReportQueueItemDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    public static bool HasLink(this HalResourceOfModerationReportDetailDto dto, string linkRel)
        => dto._links?.ContainsKey(linkRel) == true;

    // ========== Helper Methods ==========

    private static bool HasHalLinkInAdditionalProperties(IDictionary<string, object>? additionalProperties, string linkRel)
    {
        if (additionalProperties == null ||
            !additionalProperties.TryGetValue("_links", out var linksObj))
            return false;

        if (linksObj is JsonElement linksElement && linksElement.ValueKind == JsonValueKind.Object)
            return linksElement.TryGetProperty(linkRel, out _);

        return false;
    }

    private static EventListDto? MapDiscoveryItem(
        string? source,
        EventListDto? localEvent,
        FederatedEventDto? federatedEvent,
        EventFederationMetadataDto? federation,
        object? links)
    {
        EventListDto? result = localEvent;
        if (result is null && federatedEvent is not null)
        {
            result = new EventListDto
            {
                Title = federatedEvent.Name ?? "Federated event",
                Description = federatedEvent.Description,
                EventTypeFullName = "AT Protocol event",
                AudienceGenderFullName = "Community event",
                ActorDisplayName = federation?.Provenance ?? "AT Protocol",
                ActorTypeFullName = "AT Protocol identity",
                EventStatusFullName = federatedEvent.Status ?? "Published",
                VisibilityTypeFullName = "Public",
                EventFormatFullName = federatedEvent.Mode ?? federatedEvent.LocationSummary ?? "Community event",
                FirstSessionDate = federatedEvent.StartsAtUtc,
                FirstSessionStartUtc = federatedEvent.StartsAtUtc,
                LastSessionDate = federatedEvent.EndsAtUtc ?? federatedEvent.StartsAtUtc,
                CreatedAtUtc = federatedEvent.CreatedAtUtc,
                AtprotoRecordId = federation?.AtprotoRecordId ?? federatedEvent.Id,
                ParticipationConfiguration = new EventParticipationConfigurationDto
                {
                    ParticipationHandlingModeId = federatedEvent.RsvpExpected == true ? 3 : 1,
                    AdvanceRegistrationObligationId = federatedEvent.RsvpExpected == true ? 3 : 1
                }
            };
        }

        if (result is null)
            return null;

        result.AdditionalProperties["eventDiscoverySource"] =
            federatedEvent is null ? source ?? "local" : "atproto";
        if (links is not null)
            result.AdditionalProperties["_links"] = links;

        return result;
    }

    /// <summary>
    /// Deserializes a collection of generated HAL resource objects to a strongly-typed collection.
    /// Supports the legacy ICollection&lt;object&gt; shape and the typed HalResourceOf* item shape now emitted
    /// by the OpenAPI contract.
    /// </summary>
    private static ICollection<T> DeserializeItems<T>(System.Collections.IEnumerable items) where T : class
    {
        var result = new List<T>();
        foreach (var item in items)
        {
            if (item is T typedItem)
            {
                // Already the correct type - best case
                result.Add(typedItem);
            }
            else if (item is JsonElement jsonElement)
            {
                // System.Text.Json path (NSwag SystemTextJson generator)
                var deserialized = JsonSerializer.Deserialize<T>(
                    jsonElement.GetRawText(),
                    JsonOptions);
                if (deserialized != null)
                    result.Add(deserialized);
            }
            else
            {
                // Fallback: JSON serialization roundtrip for unknown types
                var json = JsonSerializer.Serialize(item, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (deserialized != null)
                    result.Add(deserialized);
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, HalLink>? ToClientLinks(IDictionary<string, HalLink>? links)
    {
        if (links is null || links.Count == 0)
            return null;

        return links.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
