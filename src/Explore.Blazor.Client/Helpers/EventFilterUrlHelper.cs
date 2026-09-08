using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Events.Components;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Holds URL-derived filter values for the event list page.
/// Built from <c>[SupplyParameterFromQuery]</c> properties; passed to EventFilterBar as initial state.
/// </summary>
public sealed record EventFilterUrlState
{
    private IReadOnlyList<int>? _formatIds;
    private IReadOnlyList<int>? _eventTypeIds;
    private IReadOnlyList<Guid>? _categoryIds;
    private IReadOnlyList<Guid>? _tagIds;
    private IReadOnlyList<int>? _madhabIds;
    private IReadOnlyList<int>? _registrationModeIds;
    private IReadOnlyList<int>? _languageIds;
    private IReadOnlyList<int>? _audienceGenderIds;
    private IReadOnlyList<int>? _audienceAgeIds;
    private IReadOnlyList<int>? _genderModeIds;
    private IReadOnlyList<int>? _referencePrayerIds;

    public string? SearchTerm { get; init; }
    public Guid? ActorId { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? GroupId { get; init; }
    public string? SortBy { get; init; }
    public bool? SortDescending { get; init; }
    public TemporalView? View { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public IReadOnlyList<int>? FormatIds
    {
        get => _formatIds;
        init => _formatIds = Snapshot(value);
    }
    public IReadOnlyList<int>? EventTypeIds
    {
        get => _eventTypeIds;
        init => _eventTypeIds = Snapshot(value);
    }
    public IReadOnlyList<Guid>? CategoryIds
    {
        get => _categoryIds;
        init => _categoryIds = Snapshot(value);
    }
    public IReadOnlyList<Guid>? TagIds
    {
        get => _tagIds;
        init => _tagIds = Snapshot(value);
    }
    public IReadOnlyList<int>? MadhabIds
    {
        get => _madhabIds;
        init => _madhabIds = Snapshot(value);
    }
    public IReadOnlyList<int>? RegistrationModeIds
    {
        get => _registrationModeIds;
        init => _registrationModeIds = Snapshot(value);
    }
    public IReadOnlyList<int>? LanguageIds
    {
        get => _languageIds;
        init => _languageIds = Snapshot(value);
    }
    public IReadOnlyList<int>? AudienceGenderIds
    {
        get => _audienceGenderIds;
        init => _audienceGenderIds = Snapshot(value);
    }
    public IReadOnlyList<int>? AudienceAgeIds
    {
        get => _audienceAgeIds;
        init => _audienceAgeIds = Snapshot(value);
    }
    public IReadOnlyList<int>? GenderModeIds
    {
        get => _genderModeIds;
        init => _genderModeIds = Snapshot(value);
    }
    public IReadOnlyList<int>? ReferencePrayerIds
    {
        get => _referencePrayerIds;
        init => _referencePrayerIds = Snapshot(value);
    }
    public int? SkillLevelId { get; init; }
    public string? TechStackTag { get; init; }

    public bool HasAnyFilter =>
        !string.IsNullOrEmpty(SearchTerm) ||
        ActorId is not null ||
        OrganizationId is not null ||
        GroupId is not null ||
        SortBy is not null ||
        SortDescending is not null ||
        View is not null ||
        DateFrom is not null ||
        DateTo is not null ||
        FormatIds is { Count: > 0 } ||
        EventTypeIds is { Count: > 0 } ||
        CategoryIds is { Count: > 0 } ||
        TagIds is { Count: > 0 } ||
        MadhabIds is { Count: > 0 } ||
        RegistrationModeIds is { Count: > 0 } ||
        LanguageIds is { Count: > 0 } ||
        AudienceGenderIds is { Count: > 0 } ||
        AudienceAgeIds is { Count: > 0 } ||
        GenderModeIds is { Count: > 0 } ||
        ReferencePrayerIds is { Count: > 0 } ||
        SkillLevelId is not null ||
        !string.IsNullOrEmpty(TechStackTag);

    private static IReadOnlyList<T>? Snapshot<T>(IEnumerable<T>? values) =>
        values is null ? null : Array.AsReadOnly(values.ToArray());
}

public static class EventFilterUrlHelper
{
    public static string BuildUrl(NavigationManager navigation, EventFilterUrlState state)
    {
        var dict = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(state.SearchTerm))
            dict["q"] = state.SearchTerm;

        if (state.ActorId.HasValue)
            dict["actorId"] = state.ActorId.Value;

        if (state.OrganizationId.HasValue)
            dict["organizationId"] = state.OrganizationId.Value;

        if (state.GroupId.HasValue)
            dict["groupId"] = state.GroupId.Value;

        return navigation.GetUriWithQueryParameters("/events", dict);
    }

    /// <summary>
    /// Builds a URL from the current filter bar state using
    /// <see cref="NavigationManagerExtensions.GetUriWithQueryParameters"/>.
    /// Lists are encoded as CSV strings. Only non-default values are included.
    /// </summary>
    public static string BuildUrl(NavigationManager navigation, EventFilterBar filterBar)
    {
        var dict = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(filterBar.SearchTerm))
            dict["q"] = filterBar.SearchTerm;

        if (filterBar.SelectedSortBy is not null and not "temporal")
            dict["sortBy"] = filterBar.SelectedSortBy;

        if (filterBar.SortDescending)
            dict["sortDesc"] = true;

        if (filterBar.SelectedTemporalView != TemporalView.UpcomingAndOngoing)
            dict["view"] = filterBar.SelectedTemporalView.ToString();

        if (filterBar.SelectedDateRange?.Start != null)
            dict["dateFrom"] = DateOnly.FromDateTime(filterBar.SelectedDateRange.Start.Value)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (filterBar.SelectedDateRange?.End != null)
            dict["dateTo"] = DateOnly.FromDateTime(filterBar.SelectedDateRange.End.Value)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        AddIntCsv(dict, "formatIds", filterBar.SelectedFormatIds);
        AddIntCsv(dict, "eventTypeIds", filterBar.SelectedEventTypeIds);
        AddIntCsv(dict, "madhabIds", filterBar.SelectedMadhabIds);
        AddIntCsv(dict, "regModeIds", filterBar.SelectedRegistrationModeIds);
        AddIntCsv(dict, "langIds", filterBar.SelectedLanguageIds);
        AddIntCsv(dict, "audienceGenderIds", filterBar.SelectedAudienceGenderIds);
        AddIntCsv(dict, "audienceAgeIds", filterBar.SelectedAudienceAgeIds);

        // Islamic filters
        AddIntCsv(dict, "genderModeIds", filterBar.SelectedGenderModeIds);
        AddIntCsv(dict, "prayerIds", filterBar.SelectedReferencePrayerIds);

        // Tech filters
        if (filterBar.SelectedSkillLevel.HasValue)
            dict["skillLevel"] = (int)filterBar.SelectedSkillLevel.Value;

        if (!string.IsNullOrWhiteSpace(filterBar.TechStackTag))
            dict["techStack"] = filterBar.TechStackTag;

        // Category/tag tri-state filters (include-only for URL)
        AddGuidCsv(dict, "categoryIds", filterBar.GetCategoryFilter().IncludedCategoryIds);
        AddGuidCsv(dict, "tagIds", filterBar.GetTagFilter().IncludedTagIds);

        return navigation.GetUriWithQueryParameters("/events", dict);
    }

    /// <summary>
    /// Applies URL-derived filter state to the filter bar's public properties.
    /// Called by EventFilterBar when it receives <see cref="EventFilterUrlState"/> via its InitialFilters parameter.
    /// </summary>
    public static void ApplyToFilterBar(EventFilterBar filterBar, EventFilterUrlState state)
    {
        if (state.SearchTerm != null)
            filterBar.SearchTerm = state.SearchTerm;

        if (state.SortBy != null)
            filterBar.SelectedSortBy = state.SortBy;

        if (state.SortDescending.HasValue)
            filterBar.SortDescending = state.SortDescending.Value;

        if (state.View.HasValue)
            filterBar.SelectedTemporalView = state.View.Value;

        if (state.DateFrom.HasValue || state.DateTo.HasValue)
        {
            var start = state.DateFrom?.ToDateTime(TimeOnly.MinValue);
            var end = state.DateTo?.ToDateTime(TimeOnly.MinValue);
            filterBar.SelectedDateRange = new MudBlazor.DateRange(start, end);
        }

        if (state.FormatIds is { Count: > 0 })
            filterBar.SelectedFormatIds = new HashSet<int>(state.FormatIds);

        if (state.EventTypeIds is { Count: > 0 })
            filterBar.SelectedEventTypeIds = new HashSet<int>(state.EventTypeIds);

        if (state.MadhabIds is { Count: > 0 })
            filterBar.SelectedMadhabIds = new HashSet<int>(state.MadhabIds);

        if (state.RegistrationModeIds is { Count: > 0 })
            filterBar.SelectedRegistrationModeIds = new HashSet<int>(state.RegistrationModeIds);

        if (state.LanguageIds is { Count: > 0 })
            filterBar.SelectedLanguageIds = new HashSet<int>(state.LanguageIds);

        if (state.AudienceGenderIds is { Count: > 0 })
            filterBar.SelectedAudienceGenderIds = new HashSet<int>(state.AudienceGenderIds);

        if (state.AudienceAgeIds is { Count: > 0 })
            filterBar.SelectedAudienceAgeIds = new HashSet<int>(state.AudienceAgeIds);

        // Islamic
        if (state.GenderModeIds is { Count: > 0 })
            filterBar.SelectedGenderModeIds = new HashSet<int>(state.GenderModeIds);

        if (state.ReferencePrayerIds is { Count: > 0 })
            filterBar.SelectedReferencePrayerIds = new HashSet<int>(state.ReferencePrayerIds);

        // Tech
        if (state.SkillLevelId.HasValue)
            filterBar.SelectedSkillLevel = (SkillLevel)state.SkillLevelId.Value;

        if (state.TechStackTag != null)
            filterBar.TechStackTag = state.TechStackTag;
    }

    // ── CSV parsing helpers for [SupplyParameterFromQuery] string? params ──

    public static List<int>? ParseIntCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var list = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, CultureInfo.InvariantCulture, out var id))
                list.Add(id);
        }
        return list.Count > 0 ? list : null;
    }

    public static List<Guid>? ParseGuidCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var list = new List<Guid>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
                list.Add(id);
        }
        return list.Count > 0 ? list : null;
    }

    public static TemporalView? ParseTemporalView(string? value) =>
        value is not null && Enum.TryParse<TemporalView>(value, ignoreCase: true, out var v) ? v : null;

    public static DateOnly? ParseDate(string? value) =>
        value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // ── Private URL building helpers ──

    private static void AddIntCsv(Dictionary<string, object?> dict, string key, IReadOnlyCollection<int>? ids)
    {
        if (ids is { Count: > 0 })
            dict[key] = string.Join(',', ids);
    }

    private static void AddGuidCsv(Dictionary<string, object?> dict, string key, IEnumerable<Guid>? ids)
    {
        var list = ids?.ToList();
        if (list is { Count: > 0 })
            dict[key] = string.Join(',', list);
    }
}
