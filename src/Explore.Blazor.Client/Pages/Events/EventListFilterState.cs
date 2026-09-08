using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Pages.Events;

internal sealed record EventListFilterState(
    string? SearchTerm,
    List<Guid>? IncludedCategoryIds,
    List<Guid>? ExcludedCategoryIds,
    string? CategoryInclusionMode,
    string? CategoryExclusionMode,
    List<Guid>? IncludedTagIds,
    List<Guid>? ExcludedTagIds,
    string? TagInclusionMode,
    string? TagExclusionMode,
    List<int>? FormatIds,
    List<int>? MadhabIds,
    List<int>? RegistrationModeIds,
    List<int>? LanguageIds,
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    string? SortBy,
    bool? SortDescending,
    List<int>? EventTypeIds,
    List<int>? AudienceGenderIds,
    List<int>? AudienceAgeIds,
    List<int>? GenderModeIds,
    List<int>? ReferencePrayerIds,
    int? SkillLevelId,
    string? TechStackTag,
    Guid? ActorId,
    Guid? OrganizationId,
    Guid? GroupId)
{
    public TemporalView? View { get; init; }
    public static EventListFilterState From(
        EventFilterBar? filterBar,
        string? searchQuery,
        Guid? actorId,
        Guid? organizationId,
        Guid? groupId)
    {
        var categoryFilter = filterBar?.GetCategoryFilter();
        var tagFilter = filterBar?.GetTagFilter();

        return new EventListFilterState(
            SearchTerm: filterBar?.SearchTerm ?? searchQuery,
            IncludedCategoryIds: categoryFilter?.IncludedCategoryIds,
            ExcludedCategoryIds: categoryFilter?.ExcludedCategoryIds,
            CategoryInclusionMode: categoryFilter?.InclusionMode,
            CategoryExclusionMode: categoryFilter?.ExclusionMode,
            IncludedTagIds: tagFilter?.IncludedTagIds,
            ExcludedTagIds: tagFilter?.ExcludedTagIds,
            TagInclusionMode: tagFilter?.InclusionMode,
            TagExclusionMode: tagFilter?.ExclusionMode,
            FormatIds: filterBar?.SelectedFormatIds?.ToList(),
            MadhabIds: filterBar?.SelectedMadhabIds?.ToList(),
            RegistrationModeIds: filterBar?.SelectedRegistrationModeIds?.ToList(),
            LanguageIds: filterBar?.SelectedLanguageIds?.ToList(),
            DateFrom: ToStartDateOffset(filterBar?.SelectedDateRange?.Start),
            DateTo: ToInclusiveEndDateOffset(filterBar?.SelectedDateRange?.End),
            SortBy: filterBar?.SelectedSortBy ?? "date",
            SortDescending: filterBar?.SortDescending ?? true,
            EventTypeIds: filterBar?.SelectedEventTypeIds?.ToList(),
            AudienceGenderIds: filterBar?.SelectedAudienceGenderIds?.ToList(),
            AudienceAgeIds: filterBar?.SelectedAudienceAgeIds?.ToList(),
            GenderModeIds: filterBar?.SelectedGenderModeIds?.ToList(),
            ReferencePrayerIds: filterBar?.SelectedReferencePrayerIds?.ToList(),
            SkillLevelId: filterBar?.SelectedSkillLevel is null ? null : (int?)filterBar.SelectedSkillLevel,
            TechStackTag: filterBar?.TechStackTag,
            ActorId: actorId,
            OrganizationId: organizationId,
            GroupId: groupId)
        {
            View = filterBar?.SelectedTemporalView
        };
    }

    public Task<PaginatedResult<EventListDto>> FetchPageAsync(
        IEventService eventService,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventService);

        return eventService.GetEventsPagedAsync(
            pageNumber,
            pageSize,
            searchTerm: SearchTerm,
            includedCategoryIds: IncludedCategoryIds,
            excludedCategoryIds: ExcludedCategoryIds,
            categoryInclusionMode: CategoryInclusionMode,
            categoryExclusionMode: CategoryExclusionMode,
            includedTagIds: IncludedTagIds,
            excludedTagIds: ExcludedTagIds,
            inclusionMode: TagInclusionMode,
            exclusionMode: TagExclusionMode,
            formatIds: FormatIds,
            madhabIds: MadhabIds,
            registrationModeIds: RegistrationModeIds,
            languageIds: LanguageIds,
            dateFrom: DateFrom,
            dateTo: DateTo,
            sortBy: SortBy,
            sortDescending: SortDescending,
            view: View?.ToString(),
            eventTypeIds: EventTypeIds,
            audienceGenderIds: AudienceGenderIds,
            audienceAgeIds: AudienceAgeIds,
            eventStatusIds: null,
            genderModeIds: GenderModeIds,
            includesQuranRecitation: null,
            referencePrayerIds: ReferencePrayerIds,
            islamicPrimaryLanguageIds: null,
            hasIslamicAspect: null,
            skillLevelId: SkillLevelId,
            isCodingCompetition: null,
            isHackathon: null,
            requiresLaptop: null,
            techStackTag: TechStackTag,
            hasTechAspect: null,
            actorId: ActorId,
            organizationId: OrganizationId,
            groupId: GroupId,
            cancellationToken: cancellationToken);
    }

    private static DateTimeOffset? ToStartDateOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(value.Value, TimeSpan.Zero);

    private static DateTimeOffset? ToInclusiveEndDateOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(value.Value.AddDays(1).AddTicks(-1), TimeSpan.Zero);
}
