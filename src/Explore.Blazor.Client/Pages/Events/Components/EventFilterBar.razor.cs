using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using MudBlazor.Services;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class EventFilterBar : IBrowserViewportObserver, IAsyncDisposable
{
    [Inject] private IBrowserViewportService BrowserViewportService { get; set; } = null!;

    // Mobile responsive state
    private bool _isMobile;
    private bool _mobileDrawerOpen;
    private bool _viewportSubscribed;
    [Parameter] public bool IsIslamicModuleEnabled { get; set; }
    [Parameter] public bool IsTechModuleEnabled { get; set; }
    [Parameter] public EventCallback OnSearchRequested { get; set; }
    [Parameter] public LayoutMode CurrentLayout { get; set; } = LayoutMode.DetailedList;
    [Parameter] public EventCallback<LayoutMode> CurrentLayoutChanged { get; set; }
    [Parameter] public int ResultCount { get; set; }
    [Parameter] public bool ShowResultCount { get; set; }
    [Parameter] public bool ShowCustomizationButton { get; set; }
    [Parameter] public EventCallback OnCustomizationRequested { get; set; }

    // Data Sources
    [Parameter] public ICollection<EventTypeListDto> EventTypes { get; set; } = new List<EventTypeListDto>();
    [Parameter] public ICollection<CategoryListDto> Categories { get; set; } = new List<CategoryListDto>();
    [Parameter] public ICollection<CategoryTypeWithCategoriesDto> CategoryGroups { get; set; } = new List<CategoryTypeWithCategoriesDto>();
    [Parameter] public ICollection<EventFormatListDto> EventFormats { get; set; } = new List<EventFormatListDto>();
    [Parameter] public ICollection<MadhabListDto> Madhabs { get; set; } = new List<MadhabListDto>();
    [Parameter] public ICollection<RegistrationModeListDto> RegistrationModes { get; set; } = new List<RegistrationModeListDto>();
    [Parameter] public ICollection<LanguageListDto> Languages { get; set; } = new List<LanguageListDto>();
    [Parameter] public ICollection<TagListDto> Tags { get; set; } = new List<TagListDto>();
    [Parameter] public ICollection<AudienceGenderListDto> AudienceGenders { get; set; } = new List<AudienceGenderListDto>();
    [Parameter] public ICollection<AudienceAgeListDto> AudienceAges { get; set; } = new List<AudienceAgeListDto>();
    [Parameter] public ICollection<TagTypeWithTagsDto> TagGroups { get; set; } = new List<TagTypeWithTagsDto>();

    // Collapse State
    private bool _filtersExpanded;

    // Filter State
    public DateRange? SelectedDateRange { get; set; }
    public string? SearchTerm { get; set; }
    public IReadOnlyCollection<int> SelectedFormatIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<int> SelectedMadhabIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<int> SelectedRegistrationModeIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<int> SelectedLanguageIds { get; set; } = new HashSet<int>();

    // Core Filters
    public IReadOnlyCollection<int> SelectedEventTypeIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<int> SelectedAudienceGenderIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<int> SelectedAudienceAgeIds { get; set; } = new HashSet<int>();
    public string SelectedSortBy { get; set; } = "date";
    public bool SortDescending { get; set; } = true;

    // Islamic Filters
    public IReadOnlyCollection<int> SelectedGenderModeIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<int> SelectedReferencePrayerIds { get; set; } = new HashSet<int>();

    // Tech Filters
    public SkillLevel? SelectedSkillLevel { get; set; }
    public string? TechStackTag { get; set; }

    // Temporal View
    public TemporalView SelectedTemporalView { get; set; } = TemporalView.UpcomingAndOngoing;

    private TriStateTagFilterDropdown? _tagFilterDropdown;
    private TriStateCategoryFilterDropdown? _categoryFilterDropdown;

    private static string GetDateClasses(DateTime date) =>
        date.Date < DateTime.Today ? "filter-bar__date--past" : string.Empty;

    private void ToggleFilters() => ToggleFilterDrawerOrPanel();

    private async Task OnLayoutChanged(LayoutMode mode)
    {
        CurrentLayout = mode;
        await CurrentLayoutChanged.InvokeAsync(mode);
    }

    private async Task SearchAsync()
    {
        await OnSearchRequested.InvokeAsync();
    }

    private async Task HandleSearchKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await SearchAsync();
        }
    }

    public TagFilterChangedEventArgs GetTagFilter()
    {
        return _tagFilterDropdown?.GetCurrentFilter() ?? new TagFilterChangedEventArgs();
    }

    public CategoryFilterChangedEventArgs GetCategoryFilter()
    {
        return _categoryFilterDropdown?.GetCurrentFilter() ?? new CategoryFilterChangedEventArgs();
    }

    private async Task ClearAllFilters()
    {
        SelectedDateRange = null;
        SearchTerm = null;
        SelectedFormatIds = new HashSet<int>();
        SelectedMadhabIds = new HashSet<int>();
        SelectedRegistrationModeIds = new HashSet<int>();
        SelectedLanguageIds = new HashSet<int>();
        SelectedEventTypeIds = new HashSet<int>();
        SelectedAudienceGenderIds = new HashSet<int>();
        SelectedAudienceAgeIds = new HashSet<int>();
        SelectedSortBy = "date";
        SortDescending = true;

        SelectedGenderModeIds = new HashSet<int>();
        SelectedReferencePrayerIds = new HashSet<int>();

        SelectedSkillLevel = null;
        TechStackTag = null;

        _tagFilterDropdown?.ResetAll();
        _categoryFilterDropdown?.ResetAll();

        await OnSearchRequested.InvokeAsync();
    }

    public int GetActiveFilterCount()
    {
        int count = 0;
        if (SelectedDateRange?.Start != null || SelectedDateRange?.End != null) count++;
        if (!string.IsNullOrEmpty(SearchTerm)) count++;
        if (SelectedFormatIds.Any()) count++;
        if (SelectedMadhabIds.Any()) count++;
        if (SelectedRegistrationModeIds.Any()) count++;
        if (SelectedLanguageIds.Any()) count++;
        if (SelectedEventTypeIds.Any()) count++;
        if (SelectedAudienceGenderIds.Any()) count++;
        if (SelectedAudienceAgeIds.Any()) count++;

        if (SelectedGenderModeIds.Any()) count++;
        if (SelectedReferencePrayerIds.Any()) count++;

        if (SelectedSkillLevel.HasValue) count++;
        if (!string.IsNullOrEmpty(TechStackTag)) count++;

        var tagFilter = GetTagFilter();
        count += tagFilter.IncludedTagIds.Count + tagFilter.ExcludedTagIds.Count;

        var categoryFilter = GetCategoryFilter();
        count += categoryFilter.IncludedCategoryIds.Count + categoryFilter.ExcludedCategoryIds.Count;

        return count;
    }

    private string FormatSelectedFormats(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        EventFormats,
        format => format.Id,
        format => format.FullName);

    private string FormatSelectedEventTypes(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        EventTypes,
        eventType => eventType.Id,
        eventType => eventType.FullName);

    private string FormatSelectedMadhabs(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        Madhabs,
        madhab => madhab.Id,
        madhab => madhab.FullName);

    private string FormatSelectedRegistrationModes(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        RegistrationModes,
        mode => mode.Id,
        mode => mode.FullName);

    private string FormatSelectedLanguages(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        Languages,
        language => language.Id,
        language => language.FullName);

    private string FormatSelectedAudienceGenders(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        AudienceGenders,
        gender => gender.Id,
        gender => gender.FullName);

    private string FormatSelectedAudienceAges(IReadOnlyList<string> selectedValues) => FormatSelectedIntLookup(
        selectedValues,
        AudienceAges,
        age => age.Id,
        age => age.FullName);

    private static string FormatSelectedGenderModes(IReadOnlyList<string> selectedValues) => FormatSelectedEnum<GenderSegregationMode>(selectedValues);

    private static string FormatSelectedPrayerTimes(IReadOnlyList<string> selectedValues) => FormatSelectedEnum<PrayerTime>(selectedValues);

    private static string FormatSelectedIntLookup<TLookup>(
        IEnumerable<string> selectedValues,
        IEnumerable<TLookup> lookupItems,
        Func<TLookup, int?> idSelector,
        Func<TLookup, string?> labelSelector)
    {
        var labelById = lookupItems
            .Where(item => idSelector(item).HasValue)
            .ToDictionary(item => idSelector(item)!.Value, item => labelSelector(item));

        return string.Join(", ", selectedValues.Select(value =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
            labelById.TryGetValue(id, out var label) &&
            !string.IsNullOrWhiteSpace(label)
                ? label
                : value));
    }

    private static string FormatSelectedGuidLookup<TLookup>(
        IEnumerable<string> selectedValues,
        IEnumerable<TLookup> lookupItems,
        Func<TLookup, Guid?> idSelector,
        Func<TLookup, string?> labelSelector)
    {
        var labelById = lookupItems
            .Where(item => idSelector(item).HasValue)
            .ToDictionary(item => idSelector(item)!.Value, item => labelSelector(item));

        return string.Join(", ", selectedValues.Select(value =>
            Guid.TryParse(value, out var id) &&
            labelById.TryGetValue(id, out var label) &&
            !string.IsNullOrWhiteSpace(label)
                ? label
                : value));
    }

    private static string FormatSelectedEnum<TEnum>(IEnumerable<string> selectedValues)
        where TEnum : struct, Enum
    {
        return string.Join(", ", selectedValues.Select(value =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
            Enum.IsDefined(typeof(TEnum), id)
                ? Enum.ToObject(typeof(TEnum), id).ToString()
                : value));
    }

    // ── IBrowserViewportObserver ──

    Guid IBrowserViewportObserver.Id { get; } = Guid.CreateVersion7();

    ResizeOptions IBrowserViewportObserver.ResizeOptions { get; } = new()
    {
        ReportRate = 250,
        NotifyOnBreakpointOnly = true
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_viewportSubscribed)
        {
            _viewportSubscribed = true;
            await BrowserViewportService.SubscribeAsync(this, fireImmediately: true);
        }
    }

    Task IBrowserViewportObserver.NotifyBrowserViewportChangeAsync(BrowserViewportEventArgs args)
    {
        var wasMobile = _isMobile;
        _isMobile = args.Breakpoint is Breakpoint.Xs or Breakpoint.Sm;

        if (wasMobile && !_isMobile)
        {
            // Switching to desktop: transfer drawer state to panel
            if (_mobileDrawerOpen)
            {
                _filtersExpanded = true;
                _mobileDrawerOpen = false;
            }
        }
        else if (!wasMobile && _isMobile)
        {
            // Switching to mobile: transfer panel state to drawer
            if (_filtersExpanded)
            {
                _mobileDrawerOpen = true;
                _filtersExpanded = false;
            }
        }

        return InvokeAsync(StateHasChanged);
    }

    private void ToggleFilterDrawerOrPanel()
    {
        if (_isMobile)
        {
            _mobileDrawerOpen = !_mobileDrawerOpen;
        }
        else
        {
            _filtersExpanded = !_filtersExpanded;
        }
    }

    private void CloseMobileDrawer() => _mobileDrawerOpen = false;

    private void OnMobileDrawerOpenChanged(bool open) => _mobileDrawerOpen = open;

    private async Task ApplyMobileFilters()
    {
        _mobileDrawerOpen = false;
        await OnSearchRequested.InvokeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_viewportSubscribed)
        {
            await BrowserViewportService.UnsubscribeAsync(this);
        }
    }
}
