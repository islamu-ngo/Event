using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetEventListRequest : IRequest<PaginatedResult<EventListDto>>
{
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the page number (1-based). Defaults to 1.
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Gets or sets the page size. Defaults to 20.
    /// </summary>
    public int PageSize { get; init; } = 20;

    // ===== Filter Parameters =====

    /// <summary>
    /// Free-text search across event title and description.
    /// </summary>
    public string? SearchTerm { get; init; }

    /// <summary>
    /// Filter by actor ownership directly.
    /// </summary>
    public Guid? ActorId { get; init; }

    /// <summary>
    /// Filter by organization ownership after resolving the organization actor.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Filter by group ownership after resolving the group actor.
    /// </summary>
    public Guid? GroupId { get; init; }

    /// <summary>
    /// Filter by category (via EventCategories junction table).
    /// When IncludedCategoryIds is also provided, this is ignored.
    /// </summary>
    public Guid? CategoryId { get; init; }

    /// <summary>
    /// Category IDs to include (events must match these categories).
    /// Combined using <see cref="CategoryInclusionMode"/> (AND = all categories, OR = any category).
    /// </summary>
    private IReadOnlyList<Guid>? _includedCategoryIds;

    public IReadOnlyList<Guid>? IncludedCategoryIds
    {
        get => _includedCategoryIds;
        init => _includedCategoryIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Category IDs to exclude (events matching these categories are filtered out).
    /// Combined using <see cref="CategoryExclusionMode"/> (AND = all categories, OR = any category).
    /// </summary>
    private IReadOnlyList<Guid>? _excludedCategoryIds;

    public IReadOnlyList<Guid>? ExcludedCategoryIds
    {
        get => _excludedCategoryIds;
        init => _excludedCategoryIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// How included categories are combined. AND = event must have all categories. OR = event must have at least one.
    /// Defaults to AND.
    /// </summary>
    public TagFilterMode CategoryInclusionMode { get; init; } = TagFilterMode.And;

    /// <summary>
    /// How excluded categories are combined. OR = exclude if event has any category. AND = exclude only if event has all categories.
    /// Defaults to OR.
    /// </summary>
    public TagFilterMode CategoryExclusionMode { get; init; } = TagFilterMode.Or;

    /// <summary>
    /// Tag IDs to include (events must match these tags).
    /// Combined using <see cref="InclusionMode"/> (AND = all tags, OR = any tag).
    /// </summary>
    private IReadOnlyList<Guid>? _includedTagIds;

    public IReadOnlyList<Guid>? IncludedTagIds
    {
        get => _includedTagIds;
        init => _includedTagIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Tag IDs to exclude (events matching these tags are filtered out).
    /// Combined using <see cref="ExclusionMode"/> (AND = all tags, OR = any tag).
    /// </summary>
    private IReadOnlyList<Guid>? _excludedTagIds;

    public IReadOnlyList<Guid>? ExcludedTagIds
    {
        get => _excludedTagIds;
        init => _excludedTagIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// How included tags are combined. AND = event must have all tags. OR = event must have at least one.
    /// Defaults to AND.
    /// </summary>
    public TagFilterMode InclusionMode { get; init; } = TagFilterMode.And;

    /// <summary>
    /// How excluded tags are combined. OR = exclude if event has any tag. AND = exclude only if event has all tags.
    /// Defaults to OR.
    /// </summary>
    public TagFilterMode ExclusionMode { get; init; } = TagFilterMode.Or;

    /// <summary>
    /// Filter by event format (online, in-person, hybrid).
    /// Supports multiple values — events matching any of the specified formats are returned.
    /// </summary>
    private IReadOnlyList<int>? _formatIds;

    public IReadOnlyList<int>? FormatIds
    {
        get => _formatIds;
        init => _formatIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by madhab.
    /// Supports multiple values — events matching any of the specified madhabs are returned.
    /// </summary>
    private IReadOnlyList<int>? _madhabIds;

    public IReadOnlyList<int>? MadhabIds
    {
        get => _madhabIds;
        init => _madhabIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by location (via EventSessions junction).
    /// Supports multiple values — events at any of the specified locations are returned.
    /// </summary>
    private IReadOnlyList<Guid>? _locationIds;

    public IReadOnlyList<Guid>? LocationIds
    {
        get => _locationIds;
        init => _locationIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by registration mode (via EventSessions junction).
    /// Supports multiple values — events with any of the specified modes are returned.
    /// </summary>
    private IReadOnlyList<int>? _registrationModeIds;

    public IReadOnlyList<int>? RegistrationModeIds
    {
        get => _registrationModeIds;
        init => _registrationModeIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by language (via EventSessions → EventSessionLanguages junction).
    /// Supports multiple values — events in any of the specified languages are returned.
    /// </summary>
    private IReadOnlyList<int>? _languageIds;

    public IReadOnlyList<int>? LanguageIds
    {
        get => _languageIds;
        init => _languageIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter events with first session date on or after this date.
    /// </summary>
    public DateOnly? DateFrom { get; init; }

    /// <summary>
    /// Filter events with first session date on or before this date.
    /// </summary>
    public DateOnly? DateTo { get; init; }

    /// <summary>
    /// Filter by event type.
    /// Supports multiple values — events matching any of the specified types are returned.
    /// </summary>
    private IReadOnlyList<int>? _eventTypeIds;

    public IReadOnlyList<int>? EventTypeIds
    {
        get => _eventTypeIds;
        init => _eventTypeIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by audience gender.
    /// Supports multiple values — events matching any of the specified genders are returned.
    /// </summary>
    private IReadOnlyList<int>? _audienceGenderIds;

    public IReadOnlyList<int>? AudienceGenderIds
    {
        get => _audienceGenderIds;
        init => _audienceGenderIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by audience age group.
    /// Supports multiple values — events matching any of the specified ages are returned.
    /// </summary>
    private IReadOnlyList<int>? _audienceAgeIds;

    public IReadOnlyList<int>? AudienceAgeIds
    {
        get => _audienceAgeIds;
        init => _audienceAgeIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by event status.
    /// Supports multiple values — events matching any of the specified statuses are returned.
    /// </summary>
    private IReadOnlyList<int>? _eventStatusIds;

    public IReadOnlyList<int>? EventStatusIds
    {
        get => _eventStatusIds;
        init => _eventStatusIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    // ===== Islamic Aspect Filter Parameters =====
    // These are only applied when the Islamic module is enabled for the tenant.

    /// <summary>
    /// Filter by Islamic aspect gender segregation mode.
    /// Supports multiple values — events matching any of the specified modes are returned.
    /// Only applied when the Islamic module ("Mod_Islamic") is enabled.
    /// </summary>
    private IReadOnlyList<int>? _genderModeIds;

    public IReadOnlyList<int>? GenderModeIds
    {
        get => _genderModeIds;
        init => _genderModeIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter events that include Quran recitation (Islamic aspect).
    /// Only applied when the Islamic module ("Mod_Islamic") is enabled.
    /// </summary>
    public bool? IncludesQuranRecitation { get; init; }

    /// <summary>
    /// Filter by reference prayer time for scheduling (Islamic aspect).
    /// Supports multiple values — events matching any of the specified prayers are returned.
    /// Only applied when the Islamic module ("Mod_Islamic") is enabled.
    /// </summary>
    private IReadOnlyList<int>? _referencePrayerIds;

    public IReadOnlyList<int>? ReferencePrayerIds
    {
        get => _referencePrayerIds;
        init => _referencePrayerIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter by Islamic content primary language (Islamic aspect).
    /// Supports multiple values — events matching any of the specified languages are returned.
    /// Only applied when the Islamic module ("Mod_Islamic") is enabled.
    /// </summary>
    private IReadOnlyList<int>? _islamicPrimaryLanguageIds;

    public IReadOnlyList<int>? IslamicPrimaryLanguageIds
    {
        get => _islamicPrimaryLanguageIds;
        init => _islamicPrimaryLanguageIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Filter to only show events with an Islamic aspect configured.
    /// Only applied when the Islamic module ("Mod_Islamic") is enabled.
    /// </summary>
    public bool? HasIslamicAspect { get; init; }

    // ===== Tech Aspect Filter Parameters =====
    // These are only applied when the Tech module is enabled for the tenant.

    /// <summary>
    /// Filter by required skill level (Tech aspect).
    /// Only applied when the Tech module ("Mod_Tech") is enabled.
    /// </summary>
    public int? SkillLevelId { get; init; }

    /// <summary>
    /// Filter to only show coding competitions (Tech aspect).
    /// Only applied when the Tech module ("Mod_Tech") is enabled.
    /// </summary>
    public bool? IsCodingCompetition { get; init; }

    /// <summary>
    /// Filter to only show hackathons (Tech aspect).
    /// Only applied when the Tech module ("Mod_Tech") is enabled.
    /// </summary>
    public bool? IsHackathon { get; init; }

    /// <summary>
    /// Filter events that require a laptop (Tech aspect).
    /// Only applied when the Tech module ("Mod_Tech") is enabled.
    /// </summary>
    public bool? RequiresLaptop { get; init; }

    /// <summary>
    /// Filter by tech stack tag (case-insensitive contains search on TechStackTags).
    /// Only applied when the Tech module ("Mod_Tech") is enabled.
    /// </summary>
    public string? TechStackTag { get; init; }

    /// <summary>
    /// Filter to only show events with a Tech aspect configured.
    /// Only applied when the Tech module ("Mod_Tech") is enabled.
    /// </summary>
    public bool? HasTechAspect { get; init; }

    // ===== Custom Property Projection Filters (Layer 3 — tenant-gated) =====

    private IReadOnlyList<CustomPropertyFilterCriterion>? _customPropertyFilters;

    public IReadOnlyList<CustomPropertyFilterCriterion>? CustomPropertyFilters
    {
        get => _customPropertyFilters;
        init => _customPropertyFilters = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    public string? CustomPropertySearchTerm { get; init; }

    // ===== Sort Parameters =====

    /// <summary>
    /// Sort field name. Supported values: "date", "title", "views", "createdAt".
    /// Defaults to "date" (FirstSessionDate descending).
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>
    /// Sort direction. True for descending, false for ascending. Defaults to true.
    /// </summary>
    public bool SortDescending { get; init; } = true;


    /// <summary>
    /// Gets or sets the temporal view filter.
    /// </summary>
    public TemporalView? View { get; init; }

    public GetEventListRequest CopyWithPagination(int pageNumber, int pageSize)
    {
        return this with
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }
}
