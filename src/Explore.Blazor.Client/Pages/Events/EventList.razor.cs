using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using MudBlazor;
using Timer = System.Threading.Timer;

namespace Explore.Blazor.Client.Pages.Events;

public partial class EventList : ComponentBase, IAsyncDisposable
{
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected IEventService EventService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.IEventSessionService EventSessionService { get; set; } = null!;
    [Inject] protected ICategoryService CategoryService { get; set; } = null!;
    [Inject] protected ITagService TagService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService EventLookupService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.IDemographicLookupService DemographicLookupService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.ICultureLookupService CultureLookupService { get; set; } = null!;
    [Inject] protected IDialogService DialogService { get; set; } = null!;
    [Inject] protected IPublicExperienceService PublicExperienceService { get; set; } = null!;
    [Inject] protected ILogger<EventList> Logger { get; set; } = null!;
    [Inject] protected IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;
    [Inject] protected IBrowserActionInterop BrowserActionInterop { get; set; } = null!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AnnouncerService { get; set; } = default!;
    [Inject] private IUserSettingsService UserSettingsService { get; set; } = default!;
    [Inject] private FeatureStateContainer FeatureState { get; set; } = default!;
    [Inject] private DockLayoutState DockLayoutState { get; set; } = default!;
    [Inject] private IDockLayoutPersistence DockLayoutPersistence { get; set; } = default!;

    [PersistentState]
    public EventListState? PersistedState { get; set; }

    private EventFilterBar? _filterBar;
    private string? _errorMessage;
    private string? _successMessage;
    private string? _searchTerm;
    private bool _filtersOpen;
    private LayoutMode _currentLayout = LayoutMode.DetailedList;

    // Module Flags
    private bool _isIslamicModuleEnabled;
    private bool _isTechModuleEnabled;
    private bool _eventCardClickOpensDetailPage;

    // Detail preview state (rendered through workspace dock inspector)
    private bool _detailDrawerOpen;
    private EventListDto? _selectedEvent;
    private bool _detailImageLoadFailed;
    private bool _isDetailImageLoading;
    private EventDto? _selectedEventDetail;
    private ICollection<EventSessionListDto>? _selectedEventSessions;
    private bool _isLoadingDetail;

    private bool isLoading = true;
    private bool _eventsLoaded = false;
    private bool _dataLoaded = false;
    private bool _usePersistedEvents = false;
    private bool _virtualizeRefreshed = false;
    private bool _useInitialBatch = false;
    private PaginatedResult<EventListDto>? _initialBatch;
    private PublicExperienceShellDto? _publicExperienceShell;

    // Pagination / Browse mode state
    private BrowseMode _browseMode = BrowseMode.InfiniteScroll;
    private int _currentPage = 1;
    private int _pageSize = 20;
    private List<EventListDto> _pagedEvents = new();
    private bool _isLoadingPage;
    private bool _isInitialized;

    // Customization drawer state
    private bool _customizationDrawerOpen;
    private ICollection<EffectiveSettingDto>? _userSettings;
    private Dictionary<string, bool> _cardFieldVisibility = new();
    private bool _showCustomizationButton;

    // Autosave debounce (500ms)
    private Timer? _autosaveTimer;
    private readonly Dictionary<string, string> _pendingChanges = new();
    private readonly object _pendingChangesLock = new();
    private bool _isSaving;
    private readonly EventListSelectionController _selectionController = new();
    private EventListDockingController? _dockingController;

    private Virtualize<EventListDto>? _virtualize;
    private int _totalCount;
    private IJSObjectReference? _imagePreloaderModule;

    // API Data
    private ICollection<EventTypeListDto> eventTypes = new List<EventTypeListDto>();
    private ICollection<AudienceGenderListDto> audienceGenders = new List<AudienceGenderListDto>();
    private ICollection<AudienceAgeListDto> audienceAges = new List<AudienceAgeListDto>();
    private ICollection<EventFormatListDto> eventFormats = new List<EventFormatListDto>();
    private ICollection<CategoryListDto> categories = new List<CategoryListDto>();
    private ICollection<TagListDto> tags = new List<TagListDto>();
    private ICollection<MadhabListDto> madhabs = new List<MadhabListDto>();
    private ICollection<RegistrationModeListDto> registrationModes = new List<RegistrationModeListDto>();
    private ICollection<LanguageListDto> languages = new List<LanguageListDto>();
    private ICollection<TagTypeWithTagsDto> tagGroups = new List<TagTypeWithTagsDto>();
    private ICollection<CategoryTypeWithCategoriesDto> categoryGroups = new List<CategoryTypeWithCategoriesDto>();

    private Dictionary<int, string> eventTypeMap = new();
    private Dictionary<int, string> eventFormatMap = new();

    private IReadOnlyList<PublicExperienceEventSectionDto> CuratedEventSections => _publicExperienceShell?.EventSections?
        .Where(section => !string.IsNullOrWhiteSpace(section.Label) && !string.IsNullOrWhiteSpace(section.Url))
        .OrderBy(section => section.SortOrder)
        .ThenBy(section => section.Label, StringComparer.OrdinalIgnoreCase)
        .ToList() ?? [];

    private string EventCatalogLabel => string.IsNullOrWhiteSpace(_publicExperienceShell?.EventCatalog?.Label)
        ? "Events"
        : _publicExperienceShell.EventCatalog.Label.Trim();

    private string EmptyStateTitle => HasActiveFilters()
        ? "No matching events found"
        : "No events found";

    private string EmptyStateMessage => HasActiveFilters()
        ? "Try adjusting your filters or search query."
        : $"No {EventCatalogLabel.ToLowerInvariant()} are published yet. Check back soon.";

    private string ListResultAnnouncement => _totalCount switch
    {
        > 0 when HasActiveFilters() => $"{_totalCount} matching {EventCatalogLabel.ToLowerInvariant()} found",
        > 0 => $"{_totalCount} {EventCatalogLabel.ToLowerInvariant()} found",
        _ => EmptyStateTitle
    };

    private static string ResolvePresetIcon(string? icon)
    {
        return icon?.Trim().ToLowerInvariant() switch
        {
            "group" or "community" => Icons.Material.Filled.Groups,
            "school" or "education" => Icons.Material.Filled.School,
            "volunteer" or "service" => Icons.Material.Filled.VolunteerActivism,
            "women" or "sisters" => Icons.Material.Filled.Female,
            "youth" => Icons.Material.Filled.Face,
            _ => Icons.Material.Filled.Event
        };
    }

    private string GetPresetChipClass(PublicExperienceEventSectionDto section)
    {
        return IsCurrentPreset(section)
            ? "event-list__preset-chip event-list__preset-chip--active"
            : "event-list__preset-chip";
    }

    private string GetPresetAriaLabel(PublicExperienceEventSectionDto section)
    {
        return IsCurrentPreset(section)
            ? $"Showing {section.Label}"
            : $"Show {section.Label}";
    }

    private bool IsCurrentPreset(PublicExperienceEventSectionDto section)
    {
        if (string.IsNullOrWhiteSpace(section.Url))
        {
            return false;
        }

        var current = NormalizeRelativeUrl(Navigation.ToBaseRelativePath(Navigation.Uri));
        var target = NormalizeRelativeUrl(section.Url);
        return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var relative = url.Trim();
        if (Uri.TryCreate(relative, UriKind.Absolute, out var absoluteUri))
        {
            relative = absoluteUri.PathAndQuery;
        }

        return relative.TrimStart('/');
    }

    // Tag/Category management popup state
    private bool _showTagCatPopup;
    private TagCategoryMode _tagCatMode;
    private IReadOnlyCollection<Guid> _tagCatInitialIds = Array.Empty<Guid>();

    [SupplyParameterFromQuery(Name = "q")]
    public string? SearchQuery { get; set; }

    [SupplyParameterFromQuery(Name = "actorId")]
    public Guid? ActorIdQuery { get; set; }

    [SupplyParameterFromQuery(Name = "organizationId")]
    public Guid? OrganizationIdQuery { get; set; }

    [SupplyParameterFromQuery(Name = "groupId")]
    public Guid? GroupIdQuery { get; set; }

    [SupplyParameterFromQuery(Name = "page")]
    public int? PageParam { get; set; }

    [SupplyParameterFromQuery(Name = "pageSize")]
    public int? PageSizeParam { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Logger.LogDebug("OnInitializedAsync starting");
        _dockingController = new EventListDockingController(DockLayoutState, DockLayoutPersistence, Logger);
        _dockingController.RegisterPanels(RenderCustomizeViewPanel, RenderEventPreviewPanel);
        DockLayoutState.Changed += OnDockLayoutChanged;

        // URL params trigger pagination mode
        if (PageParam is > 0)
        {
            _browseMode = BrowseMode.Pagination;
            _currentPage = PageParam.Value;
        }

        if (PageSizeParam is > 0 and <= 50)
        {
            _browseMode = BrowseMode.Pagination;
            _pageSize = PageSizeParam.Value;
        }

        if (!string.IsNullOrEmpty(SearchQuery))
        {
            // Defer search query set until filter bar is ready or handle in LoadEvents
        }

        if (TryRestoreState())
        {
            _isInitialized = true;
            return;
        }

        var shellTask = PublicExperienceService.GetCachedShellAsync();
        _publicExperienceShell = shellTask is null ? null : await shellTask;

        var settings = await PublicExperienceService.GetSettingsAsync();
        if (settings != null)
        {
            _isIslamicModuleEnabled = settings.IsIslamicModuleEnabled == true;
            _isTechModuleEnabled = settings.IsTechModuleEnabled == true;
            _eventCardClickOpensDetailPage = settings.EventCardClickOpensDetailPage == true;
        }

        // Always load customization settings (feature flag gating removed during development)
        _showCustomizationButton = true;
        await LoadUserSettingsAsync();

        await LoadDataAsync();
        await PreloadInitialEventsAsync();
        _isInitialized = true;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_isInitialized || _browseMode != BrowseMode.Pagination) return;

        // Handle browser back/forward changing URL params
        if (PageParam is > 0 && PageParam.Value != _currentPage)
        {
            _currentPage = PageParam.Value;
            await LoadPagedEventsAsync(_currentPage);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        Logger.LogWarning("OnAfterRenderAsync: firstRender={First}, _dataLoaded={Data}, _virtualize={Virt}, _virtualizeRefreshed={Refreshed}, _eventsLoaded={Events}",
            firstRender, _dataLoaded, _virtualize != null, _virtualizeRefreshed, _eventsLoaded);

        if (firstRender)
        {
            await RequireDockingController().HydrateWorkspaceDockLayoutAsync();
        }

        // Virtualize's IntersectionObserver may not fire when it first appears
        // in a conditional render block inside MudGrid. Force the initial load.
        if (_browseMode == BrowseMode.InfiniteScroll && _dataLoaded && _virtualize != null && !_virtualizeRefreshed)
        {
            _virtualizeRefreshed = true;
            await _virtualize.RefreshDataAsync();
        }
    }

    private bool TryRestoreState()
    {
        if (PersistedState == null)
        {
            return false;
        }

        _isIslamicModuleEnabled = PersistedState.IsIslamicModuleEnabled;
        _isTechModuleEnabled = PersistedState.IsTechModuleEnabled;
        _eventCardClickOpensDetailPage = PersistedState.EventCardClickOpensDetailPage;

        eventTypes = PersistedState.EventTypes;
        audienceGenders = PersistedState.AudienceGenders;
        audienceAges = PersistedState.AudienceAges;
        eventFormats = PersistedState.EventFormats;
        categories = PersistedState.Categories;
        tags = PersistedState.Tags;
        madhabs = PersistedState.Madhabs;
        registrationModes = PersistedState.RegistrationModes;
        languages = PersistedState.Languages;
        tagGroups = PersistedState.TagGroups;
        categoryGroups = PersistedState.CategoryGroups;

        BuildLookupMaps();
        _dataLoaded = true;
        _totalCount = PersistedState.TotalCount;
        _eventsLoaded = true;
        isLoading = false;

        // Restore pagination state
        if (PersistedState.BrowseMode == BrowseMode.Pagination)
        {
            _browseMode = PersistedState.BrowseMode;
            _currentPage = PersistedState.CurrentPage;
            _pageSize = PersistedState.PageSize;
            _pagedEvents = PersistedState.InitialItems;
        }
        else
        {
            _usePersistedEvents = true;
        }

        return true;
    }

    private void BuildLookupMaps()
    {
        eventTypeMap = eventTypes.Where(et => et.Id.HasValue).ToDictionary(et => et.Id.GetValueOrDefault(), et => et.FullName);
        eventFormatMap = eventFormats.Where(pt => pt.Id.HasValue).ToDictionary(pt => pt.Id.GetValueOrDefault(), pt => pt.FullName);
    }

    private async Task LoadDataAsync()
    {
        if (_dataLoaded) return;
        isLoading = true;
        try
        {
            var eventTypesTask = EventLookupService.GetEventTypesAsync();
            var audienceGendersTask = DemographicLookupService.GetAudienceGendersAsync();
            var audienceAgesTask = DemographicLookupService.GetAudienceAgesAsync();
            var eventFormatsTask = EventLookupService.GetEventFormatsAsync();
            var categoriesTask = CategoryService.GetAllCategoriesAsync();
            var tagsTask = TagService.GetAllTagsAsync();
            var madhabsTask = CultureLookupService.GetMadhabsAsync();
            var registrationModesTask = EventLookupService.GetRegistrationModesAsync();
            var languagesTask = CultureLookupService.GetLanguagesAsync();
            var tagGroupsTask = TagService.GetTagsGroupedByTagTypeAsync();
            var categoryGroupsTask = CategoryService.GetCategoriesGroupedByCategoryTypeAsync();

            await Task.WhenAll(eventTypesTask, audienceGendersTask, audienceAgesTask, eventFormatsTask, categoriesTask, tagsTask, madhabsTask, registrationModesTask, languagesTask, tagGroupsTask, categoryGroupsTask);

            eventTypes = await eventTypesTask ?? new List<EventTypeListDto>();
            audienceGenders = await audienceGendersTask ?? new List<AudienceGenderListDto>();
            audienceAges = await audienceAgesTask ?? new List<AudienceAgeListDto>();
            eventFormats = await eventFormatsTask ?? new List<EventFormatListDto>();
            categories = await categoriesTask ?? new List<CategoryListDto>();
            tags = await tagsTask ?? new List<TagListDto>();
            madhabs = await madhabsTask ?? new List<MadhabListDto>();
            registrationModes = await registrationModesTask ?? new List<RegistrationModeListDto>();
            languages = await languagesTask ?? new List<LanguageListDto>();
            tagGroups = await tagGroupsTask ?? new List<TagTypeWithTagsDto>();
            categoryGroups = await categoryGroupsTask ?? new List<CategoryTypeWithCategoriesDto>();

            // When junction tables (CategoryTypeCategories / TagTypeTags) are empty,
            // the grouped endpoints return nothing even though categories/tags exist.
            // Fall back to a single "All" group so the TriState filter dropdowns still work.
            if (!categoryGroups.Any() && categories.Any())
            {
                categoryGroups = new List<CategoryTypeWithCategoriesDto>
                {
                    new() { Id = 0, FullName = "All Categories", Categories = categories.ToList() }
                };
                Logger.LogDebug("CategoryGroups empty; created fallback group with {Count} categories", categories.Count);
            }

            if (!tagGroups.Any() && tags.Any())
            {
                tagGroups = new List<TagTypeWithTagsDto>
                {
                    new() { Id = 0, FullName = "All Tags", Tags = tags.ToList() }
                };
                Logger.LogDebug("TagGroups empty; created fallback group with {Count} tags", tags.Count);
            }

            BuildLookupMaps();
            _dataLoaded = true;
            // Don't set isLoading = false here. Wait for the first batch of events.
            // isLoading = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "LoadDataAsync error");
            isLoading = false; // Stop loading on error
        }
    }

    private async Task PreloadInitialEventsAsync()
    {
        if (!_dataLoaded) return;

        // In pagination mode, use the paged loading path instead
        if (_browseMode == BrowseMode.Pagination)
        {
            await LoadPagedEventsAsync(_currentPage);
            return;
        }

        try
        {
            _initialBatch = await FetchEventsPagedAsync(1, 20, CancellationToken.None);

            _totalCount = _initialBatch.TotalCount;
            _useInitialBatch = true;

            // Preload images into the browser cache so cards appear with images ready
            await PreloadImagesAsync(_initialBatch.Items);

            _eventsLoaded = true;
            isLoading = false;

            await AnnouncerService.AnnouncePoliteAsync(ListResultAnnouncement);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "PreloadInitialEventsAsync error");
            // Still flip to loaded state so the page isn't stuck on skeleton
            _eventsLoaded = true;
            isLoading = false;
            await AnnouncerService.AnnounceAssertiveAsync("Failed to load events. Please try again.");
        }
    }

    private async Task PreloadImagesAsync(IEnumerable<EventListDto> events)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0) return;

        var imageUrls = eventList
            .Select(evt => string.IsNullOrEmpty(evt.FeaturedImageUri) ? GetEventImage(evt) : evt.FeaturedImageUri!)
            .ToArray();

        try
        {
            _imagePreloaderModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./js/image-preloader.js");
            await _imagePreloaderModule.InvokeVoidAsync("preloadImages", (object)imageUrls);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Image preloading failed, proceeding without it");
        }
    }

    private Task<PaginatedResult<EventListDto>> FetchEventsPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var filterState = EventListFilterState.From(
            _filterBar,
            SearchQuery,
            ActorIdQuery,
            OrganizationIdQuery,
            GroupIdQuery);

        return filterState.FetchPageAsync(EventService, pageNumber, pageSize, cancellationToken);
    }

    private async ValueTask<ItemsProviderResult<EventListDto>> LoadEventsAsync(ItemsProviderRequest request)
    {
        Logger.LogWarning("LoadEventsAsync called: StartIndex={Start}, Count={Count}, _usePersistedEvents={Persisted}, _useInitialBatch={Batch}",
            request.StartIndex, request.Count, _usePersistedEvents, _useInitialBatch);

        if (_usePersistedEvents && PersistedState != null && request.StartIndex == PersistedState.InitialStartIndex)
        {
            _usePersistedEvents = false;
            return new ItemsProviderResult<EventListDto>(PersistedState.InitialItems, PersistedState.TotalCount);
        }

        // Reuse the initial batch that was already fetched and image-preloaded
        if (_useInitialBatch && _initialBatch != null && request.StartIndex == 0)
        {
            _useInitialBatch = false;
            var batch = _initialBatch;
            _initialBatch = null;
            return new ItemsProviderResult<EventListDto>(batch.Items, batch.TotalCount);
        }

        var pageSize = Math.Max(request.Count, 20);
        var pageNumber = (request.StartIndex / pageSize) + 1;

        var result = await FetchEventsPagedAsync(pageNumber, pageSize, request.CancellationToken);

        _totalCount = result.TotalCount;
        _eventsLoaded = true;
        if (isLoading) isLoading = false;

        _selectionController.TrackLoadedEvents(result.Items);

        StateHasChanged();

        if (PersistedState == null && request.StartIndex == 0)
        {
            PersistState(result.Items.ToList(), result.TotalCount, request.StartIndex);
        }

        return new ItemsProviderResult<EventListDto>(result.Items, result.TotalCount);
    }

    private async Task LoadPagedEventsAsync(int page)
    {
        _isLoadingPage = true;
        StateHasChanged();

        try
        {
            var result = await FetchEventsPagedAsync(page, _pageSize, CancellationToken.None);
            _pagedEvents = result.Items.ToList();
            _totalCount = result.TotalCount;
            _currentPage = page;
            _eventsLoaded = true;
            isLoading = false;

            _selectionController.TrackLoadedEvents(result.Items);

            // Preload images for the new page
            await PreloadImagesAsync(result.Items);

            // Persist state for SSR handoff
            PersistState(result.Items.ToList(), result.TotalCount, 0);

            // Accessibility announcement
            var startItem = ((page - 1) * _pageSize) + 1;
            var endItem = Math.Min(page * _pageSize, _totalCount);
            await AnnouncerService.AnnouncePoliteAsync($"Showing events {startItem} to {endItem} of {_totalCount}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "LoadPagedEventsAsync error for page {Page}", page);
            _eventsLoaded = true;
            isLoading = false;
            await AnnouncerService.AnnounceAssertiveAsync("Failed to load events. Please try again.");
        }
        finally
        {
            _isLoadingPage = false;
            StateHasChanged();
        }
    }

    private void PersistState(List<EventListDto> items, int totalCount, int startIndex)
    {
        PersistedState = new EventListState
        {
            InitialItems = items,
            TotalCount = totalCount,
            InitialStartIndex = startIndex,
            BrowseMode = _browseMode,
            CurrentPage = _currentPage,
            PageSize = _pageSize,
            IsIslamicModuleEnabled = _isIslamicModuleEnabled,
            IsTechModuleEnabled = _isTechModuleEnabled,
            EventCardClickOpensDetailPage = _eventCardClickOpensDetailPage,
            EventTypes = eventTypes.ToList(),
            AudienceGenders = audienceGenders.ToList(),
            AudienceAges = audienceAges.ToList(),
            EventFormats = eventFormats.ToList(),
            Categories = categories.ToList(),
            Tags = tags.ToList(),
            Madhabs = madhabs.ToList(),
            RegistrationModes = registrationModes.ToList(),
            Languages = languages.ToList(),
            TagGroups = tagGroups.ToList(),
            CategoryGroups = categoryGroups.ToList()
        };
    }

    private async Task RefreshList()
    {
        if (_browseMode == BrowseMode.Pagination)
        {
            _currentPage = 1;
            await LoadPagedEventsAsync(1);
            UpdateUrl();
            return;
        }

        if (_virtualize != null)
        {
            _selectionController.ClearLoadedEvents();
            await _virtualize.RefreshDataAsync();
        }
    }

    private async Task OnPageChanged(int page)
    {
        _currentPage = page;
        UpdateUrl();
        await LoadPagedEventsAsync(page);
    }

    private async Task OnPageSizeChanged(int size)
    {
        _pageSize = size;
        _currentPage = 1;
        UpdateUrl();
        await LoadPagedEventsAsync(1);
    }

    private void UpdateUrl()
    {
        if (_browseMode != BrowseMode.Pagination) return;

        var queryParams = new Dictionary<string, object?>
        {
            ["page"] = _currentPage > 1 ? _currentPage : null,
            ["pageSize"] = _pageSize != 20 ? _pageSize : null,
            ["q"] = !string.IsNullOrEmpty(SearchQuery) ? SearchQuery : null,
            ["actorId"] = ActorIdQuery,
            ["organizationId"] = OrganizationIdQuery,
            ["groupId"] = GroupIdQuery
        };

        var uri = Navigation.GetUriWithQueryParameters(queryParams);
        Navigation.NavigateTo(uri, new NavigationOptions { ReplaceHistoryEntry = true });
    }

    private async Task SelectEvent(EventListDto evt)
    {
        if (_eventCardClickOpensDetailPage)
        {
            var path = EventUrlHelper.BuildPublicPath(evt.Slug, evt.PublicCode) ?? $"/events/{evt.Id}";
            Navigation.NavigateTo(path);
            return;
        }

        _selectedEvent = evt;
        _detailImageLoadFailed = false;
        _isDetailImageLoading = HasUsableFeaturedImage(evt);
        _selectedEventDetail = null;
        _selectedEventSessions = null;
        _detailDrawerOpen = true;
        _isLoadingDetail = true;
        RequireDockingController().OpenEventPreview();

        try
        {
            var detailTask = EventService.GetEventByIdAsync(evt.Id!.Value);
            var sessionsTask = EventSessionService.GetSessionsByEventAsync(evt.Id!.Value);
            await Task.WhenAll(detailTask, sessionsTask);

            _selectedEventDetail = await detailTask;
            _selectedEventSessions = await sessionsTask;
        }
        catch
        {
            // Silently fail — the panel still shows basic info from EventListDto
        }
        finally
        {
            _isLoadingDetail = false;
            await RefreshDetailPreviewAsync();
        }
    }

    private async Task CloseDetailDrawer()
    {
        _detailDrawerOpen = false;
        ClearDetailPreviewTransientState();
        RequireDockingController().CloseEventPreviewIfRegistered();
        await RefreshDetailPreviewAsync();
    }

    private void OnDetailDrawerOpenChanged(bool open)
    {
        _detailDrawerOpen = open;
        if (open)
        {
            RequireDockingController().OpenEventPreviewIfRegistered();

            return;
        }

        if (!open)
        {
            ClearDetailPreviewTransientState();
            RequireDockingController().CloseEventPreviewIfRegistered();
        }
    }

    private void ClearDetailPreviewTransientState()
    {
        _selectedEvent = null;
        _selectedEventDetail = null;
        _selectedEventSessions = null;
        _isDetailImageLoading = false;
        _showTagCatPopup = false;
    }

    private async Task HandleOutsideDrawerClick()
    {
        if (_showTagCatPopup)
        {
            _showTagCatPopup = false;
        }
        else
        {
            await CloseDetailDrawer();
        }
    }

    // ── Customization Drawer ──

    private async Task LoadUserSettingsAsync()
    {
        try
        {
            var result = await UserSettingsService.GetSettingsAsync("event-list");
            if (result?.Settings != null)
            {
                _userSettings = result.Settings;
                ApplySettingsToState();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load user settings for event-list");
        }
    }

    private void ApplySettingsToState()
    {
        if (_userSettings == null) return;

        var lookup = _userSettings.ToDictionary(s => s.Key, s => s);

        // Apply browse mode (only if not overridden by URL params)
        if (PageParam is null && lookup.TryGetValue("event_list.browse_mode", out var bm) && !string.IsNullOrEmpty(bm.Value))
        {
            _browseMode = string.Equals(bm.Value, "infinite_scroll", StringComparison.OrdinalIgnoreCase)
                ? BrowseMode.InfiniteScroll
                : BrowseMode.Pagination;
        }

        // Apply page size (only if not overridden by URL params)
        if (PageSizeParam is null && lookup.TryGetValue("event_list.page_size", out var ps) && int.TryParse(ps.Value, out var pageSize) && pageSize > 0)
        {
            _pageSize = pageSize;
        }

        // Apply layout
        if (lookup.TryGetValue("event_list.default_layout", out var layout) && !string.IsNullOrEmpty(layout.Value))
        {
            if (Enum.TryParse<LayoutMode>(layout.Value, ignoreCase: true, out var lm))
            {
                _currentLayout = lm;
            }
        }

        // Apply card field visibility
        _cardFieldVisibility = new Dictionary<string, bool>();
        string[] cardKeys =
        [
            "event_list.card.show_date", "event_list.card.show_location", "event_list.card.show_organizer",
            "event_list.card.show_description", "event_list.card.show_tags", "event_list.card.show_categories",
            "event_list.card.show_capacity", "event_list.card.show_price", "event_list.card.show_status"
        ];
        foreach (var key in cardKeys)
        {
            if (lookup.TryGetValue(key, out var s) && bool.TryParse(s.Value, out var visible))
            {
                _cardFieldVisibility[key] = visible;
            }
        }
    }

    private void OpenCustomizationDrawer()
    {
        _customizationDrawerOpen = true;
        RequireDockingController().OpenCustomizationDrawer();
    }

    private void CloseCustomizationDrawer()
    {
        _customizationDrawerOpen = false;
        RequireDockingController().CloseCustomizationDrawer();
    }

    private void OnDockLayoutChanged()
    {
        var change = RequireDockingController().SynchronizeAfterDockLayoutChanged(
            _customizationDrawerOpen,
            _detailDrawerOpen);

        _customizationDrawerOpen = change.CustomizationDrawerOpen;
        _detailDrawerOpen = change.DetailDrawerOpen;

        if (change.ShouldClearDetailPreview)
        {
            ClearDetailPreviewTransientState();
        }

        if (change.ShouldRender)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    private RenderFragment RenderCustomizeViewPanel => builder =>
    {
        builder.OpenComponent<EventListCustomizationDrawer>(0);
        builder.AddAttribute(1, nameof(EventListCustomizationDrawer.Settings), _userSettings);
        builder.AddAttribute(2, nameof(EventListCustomizationDrawer.IsSaving), _isSaving);
        builder.AddAttribute(3, nameof(EventListCustomizationDrawer.OnCloseRequested), EventCallback.Factory.Create(this, CloseCustomizationDrawer));
        builder.AddAttribute(4, nameof(EventListCustomizationDrawer.OnSettingsChanged), EventCallback.Factory.Create<Dictionary<string, string>>(this, HandleSettingsChanged));
        builder.AddAttribute(5, nameof(EventListCustomizationDrawer.OnResetRequested), EventCallback.Factory.Create(this, HandleResetSettings));
        builder.CloseComponent();
    };

    private EventListDockingController RequireDockingController()
    {
        return _dockingController
            ?? throw new InvalidOperationException("EventList docking controller has not been initialized.");
    }

    private Task HandleSettingsChanged(Dictionary<string, string> changes)
    {
        if (_userSettings == null) return Task.CompletedTask;

        // Optimistic update: apply changes to local settings immediately
        foreach (var (key, value) in changes)
        {
            var existing = _userSettings.FirstOrDefault(s => s.Key == key);
            if (existing != null)
            {
                existing.Value = value;
            }
            else
            {
                _userSettings.Add(new EffectiveSettingDto { Key = key, Value = value });
            }
        }

        ApplySettingsToState();

        // Accumulate changes for debounced save (500ms)
        lock (_pendingChangesLock)
        {
            foreach (var (key, value) in changes)
            {
                _pendingChanges[key] = value;
            }
        }

        _autosaveTimer?.Dispose();
        _autosaveTimer = new Timer(FlushPendingChanges, null, 500, Timeout.Infinite);

        return Task.CompletedTask;
    }

    private async void FlushPendingChanges(object? state)
    {
        Dictionary<string, string> changesToSave;
        lock (_pendingChangesLock)
        {
            if (_pendingChanges.Count == 0) return;
            changesToSave = new Dictionary<string, string>(_pendingChanges);
            _pendingChanges.Clear();
        }

        try
        {
            _isSaving = true;
            await InvokeAsync(StateHasChanged);

            var result = await UserSettingsService.UpdateSettingsBatchAsync("event-list", changesToSave);
            UserSettingsService.InvalidateCache("event-list");

            await InvokeAsync(() =>
            {
                _isSaving = false;
                if (result?.Results != null)
                {
                    var skipped = result.Results.Count(r => r.Applied != true);
                    if (skipped > 0)
                    {
                        Snackbar.Add($"{skipped} setting(s) skipped (locked)", Severity.Warning,
                            options => options.VisibleStateDuration = 3000);
                    }
                }
                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to save user settings batch");
            await InvokeAsync(() =>
            {
                _isSaving = false;
                Snackbar.Add("Failed to save settings", Severity.Error);
                StateHasChanged();
            });
        }
    }

    private async Task HandleResetSettings()
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Reset Settings",
            "Are you sure you want to reset all customization settings to their defaults? This cannot be undone.",
            yesText: "Reset", cancelText: "Cancel",
            options: DialogOptionsFactory.Small());

        if (confirmed != true) return;

        try
        {
            // Cancel any pending autosave
            _autosaveTimer?.Dispose();
            _autosaveTimer = null;
            lock (_pendingChangesLock) { _pendingChanges.Clear(); }

            await UserSettingsService.ResetAllAsync("event-list");
            UserSettingsService.InvalidateCache("event-list");
            await LoadUserSettingsAsync();
            Snackbar.Add("Settings reset to defaults", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to reset user settings");
            Snackbar.Add("Failed to reset settings", Severity.Error);
        }
    }

    private async Task HandleDrawerCloseClick()
    {
        if (_showTagCatPopup)
        {
            _showTagCatPopup = false;
        }
        else
        {
            await CloseDetailDrawer();
        }
    }

    private void NavigateToSelectedEventPage()
    {
        var path = EventUrlHelper.BuildPublicPath(_selectedEvent?.Slug, _selectedEvent?.PublicCode)
            ?? (_selectedEvent?.Id is { } id ? $"/events/{id}" : null);
        if (path is not null)
        {
            Navigation.NavigateTo(path);
        }
    }

    private async Task SetTagCategoryPopupVisibleAsync(bool value)
    {
        _showTagCatPopup = value;
        await RefreshDetailPreviewAsync();
    }

    private async Task CopyEventLinkAsync()
    {
        var path = EventUrlHelper.BuildPublicPath(_selectedEvent?.Slug, _selectedEvent?.PublicCode)
            ?? (_selectedEvent?.Id is { } id ? $"/events/{id}" : null);
        if (path is null) return;

        var url = CanonicalUrlHelper.Build(Navigation, path);
        if (await BrowserActionInterop.CopyTextAsync(url))
        {
            Snackbar.Add("Link copied to clipboard", Severity.Success, options => options.VisibleStateDuration = 2000);
            return;
        }

        Logger.LogWarning("Failed to copy event link to clipboard");
        Snackbar.Add("Could not copy link", Severity.Error);
    }

    private async Task ShareSelectedEventAsync()
    {
        if (_selectedEvent is null)
        {
            Snackbar.Add("Sharing is unavailable for this event.", Severity.Warning);
            return;
        }

        await ShareEventAsync(_selectedEvent);
    }

    private async Task ShareEventAsync(EventListDto eventToShare)
    {
        var path = EventUrlHelper.BuildPublicPath(eventToShare.Slug, eventToShare.PublicCode)
            ?? (eventToShare.Id is { } id ? $"/events/{id}" : null);
        if (path is null)
        {
            Snackbar.Add("Sharing is unavailable for this event.", Severity.Warning);
            return;
        }

        var url = CanonicalUrlHelper.Build(Navigation, path);

        if (await BrowserActionInterop.ShareAsync(eventToShare.Title ?? "Event", url))
        {
            return;
        }

        if (await BrowserActionInterop.CopyTextAsync(url))
        {
            Snackbar.Add("Link copied to clipboard", Severity.Success,
                options => options.VisibleStateDuration = 2000);
            return;
        }

        Logger.LogWarning("Failed to copy event link to clipboard");
        Snackbar.Add("Could not copy link", Severity.Error);
    }

    private string GetSelectedEventCalendarUrl()
    {
        return _selectedEvent?.Id is Guid eventId && eventId != Guid.Empty
            ? $"/api/event/{eventId}/calendar"
            : "#";
    }

    private void OnLayoutModeChanged(LayoutMode mode)
    {
        _currentLayout = mode;
    }

    private bool HasActiveFilters()
    {
        if (!string.IsNullOrEmpty(SearchQuery)) return true;
        if (ActorIdQuery.HasValue || OrganizationIdQuery.HasValue || GroupIdQuery.HasValue) return true;
        if (_filterBar == null) return false;
        return _filterBar.GetActiveFilterCount() > 0;
    }

    private string GetGridCssClass()
    {
        return $"mt-4 event-grid event-grid--{_currentLayout}";
    }

    private string GetCardCssClass(EventListDto evt)
    {
        var baseClass = $"event-card event-card--{_currentLayout}";
        if (_currentLayout == LayoutMode.CompactGrid)
            baseClass += " rounded-lg";
        else
            baseClass += " rounded-xl";

        if (_selectedEvent?.Id == evt.Id && _detailDrawerOpen)
            baseClass += " event-card--selected";

        return baseClass;
    }

    private (int Xs, int Sm, int Md, int Lg, int Xl) GetGridBreakpoints()
    {
        return _currentLayout switch
        {
            LayoutMode.CompactGrid => (6, 4, 3, 2, 2),
            LayoutMode.DetailedList => (12, 12, 6, 4, 4),
            LayoutMode.SingleRow => (12, 12, 12, 12, 12),
            _ => (6, 6, 4, 3, 3)
        };
    }

    private async Task ApplyCategoryFilter(CategoryListDto category)
    {
        if (_filterBar != null && category.FullName != null)
        {
            _filterBar.SearchTerm = category.FullName;
        }
        await RefreshList();
    }

    // ... (helper methods like GetSelectedCategoryName can remain or be used for display)

    private void NavigateToEdit(EventListDto evt)
    {
        if (evt.Id.HasValue)
            Navigation.NavigateTo($"/events/{evt.Id.Value}/edit");
    }

    private async Task OpenDeleteDialog(EventListDto evt)
    {
        var parameters = new DialogParameters { ["EventId"] = evt.Id, ["EventTitle"] = evt.Title };
        var options = DialogOptionsFactory.Small();
        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await DeleteEventDialog.ShowAsync(DialogService, "Delete Event", parameters, options);
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();
        if (result != null && !result.Canceled)
        {
            await _virtualize?.RefreshDataAsync()!;
        }
    }

    private string GetEventTypeName(EventListDto eventItem)
    {
        if (!string.IsNullOrEmpty(eventItem.EventTypeFullName)) return eventItem.EventTypeFullName;
        if (eventItem.EventTypeId.HasValue && eventTypeMap.TryGetValue(eventItem.EventTypeId.Value, out var eventTypeName))
            return eventTypeName;
        return "Event";
    }

    private string GetLocationText(EventListDto eventItem)
    {
        if (eventItem.EventFormatId == 2) return "Online";
        if (!string.IsNullOrEmpty(eventItem.EventFormatFullName)) return eventItem.EventFormatFullName;
        if (eventItem.EventFormatId.HasValue && eventFormatMap.TryGetValue(eventItem.EventFormatId.Value, out var formatName))
            return formatName;
        return "Location TBD";
    }

    private static string GetAudienceText(EventListDto eventItem)
    {
        var gender = string.IsNullOrWhiteSpace(eventItem.AudienceGenderFullName)
            ? "All genders"
            : eventItem.AudienceGenderFullName;
        var age = string.IsNullOrWhiteSpace(eventItem.AudienceAgeFullName)
            ? "All ages"
            : eventItem.AudienceAgeFullName;
        return $"{gender} · {age}";
    }

    private static string GetProgramCountText(EventDto detail, ICollection<EventSessionListDto>? sessions)
    {
        var count = detail.SessionCount ?? sessions?.Count ?? 0;
        return count switch
        {
            0 => "Program not published yet",
            1 => "1 program item",
            _ => $"{count} program items"
        };
    }

    private static string GetRegistrationPolicyText(EventDto detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.RegistrationPolicyFullName))
        {
            return detail.RegistrationPolicyFullName;
        }

        return detail.ParticipationConfiguration?.AdvanceRegistrationObligationName
            ?? detail.ParticipationConfiguration?.ParticipationHandlingModeName
            ?? "Participation details not specified";
    }

    private static Color GetFormatColor(EventDto detail)
    {
        if (string.Equals(detail.EventFormatMasterCode, "DIGITAL", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Info;
        }

        return string.Equals(detail.EventFormatMasterCode, "HYBRID", StringComparison.OrdinalIgnoreCase)
            ? Color.Tertiary
            : Color.Default;
    }

    private static string GetFormatIcon(EventDto detail)
    {
        if (string.Equals(detail.EventFormatMasterCode, "DIGITAL", StringComparison.OrdinalIgnoreCase))
        {
            return Icons.Material.Filled.Videocam;
        }

        return string.Equals(detail.EventFormatMasterCode, "HYBRID", StringComparison.OrdinalIgnoreCase)
            ? Icons.Material.Filled.Devices
            : Icons.Material.Filled.LocationOn;
    }

    private string GetEventImage(EventListDto eventItem)
    {
        return ImageHelper.GetEventImageUrl(eventItem.FeaturedImageUri, eventItem.Title, GetEventColorForEvent(eventItem));
    }

    private string GetDetailImageSrc()
    {
        if (_selectedEvent == null) return string.Empty;
        if (_detailImageLoadFailed || string.IsNullOrWhiteSpace(_selectedEvent.FeaturedImageUri))
            return GetFallbackEventImage(_selectedEvent);
        return _selectedEvent.FeaturedImageUri;
    }

    private bool HasDetailActualImage => _selectedEvent != null
        && HasUsableFeaturedImage(_selectedEvent)
        && !_detailImageLoadFailed;

    private bool ShouldShowDetailImageSkeleton => _isLoadingDetail || _isDetailImageLoading;

    private string GetDetailImageCssClass(bool hasDetailActualImage, bool showDetailImageSkeleton)
    {
        var cssClass = hasDetailActualImage
            ? "event-list__detail-image-actual"
            : "event-list__detail-image-fallback";

        return showDetailImageSkeleton
            ? $"{cssClass} event-list__detail-image--loading"
            : cssClass;
    }

    private string GetFallbackEventImage(EventListDto eventItem)
    {
        return ImageHelper.GetEventImageUrl(null, eventItem.Title, GetEventColorForEvent(eventItem), width: 300, height: 400);
    }

    private async Task HandleDetailImageLoaded()
    {
        if (!_isDetailImageLoading && !_isLoadingDetail)
        {
            return;
        }

        _isDetailImageLoading = false;
        await RefreshDetailPreviewAsync();
    }

    private async Task HandleDetailImageError()
    {
        _detailImageLoadFailed = true;
        _isDetailImageLoading = false;
        await RefreshDetailPreviewAsync();
    }

    private static bool HasUsableFeaturedImage(EventListDto eventItem) =>
        !string.IsNullOrWhiteSpace(eventItem.FeaturedImageUri);

    private async Task RefreshDetailPreviewAsync()
    {
        await InvokeAsync(StateHasChanged);
        DockLayoutState.Refresh();
    }

    private string GetEventColorForEvent(EventListDto eventItem)
    {
        var color = EventColorHelper.GetColorByTypeId(eventItem.EventTypeId);
        return color != EventColorHelper.DefaultColor ? color : EventColorHelper.GetColorByHash(eventItem.Title);
    }

    private string GetTruncatedDescription(string? description)
    {
        return StringHelper.TruncateDescription(description);
    }

    private string GetActorInitials(string? displayName)
    {
        return DisplayHelper.GetInitials(displayName);
    }

    private static Color GetStatusColor(string? masterCode) => masterCode switch
    {
        "PUBLISHED" => Color.Success,
        "DRAFT" => Color.Default,
        "CANCELLED" => Color.Error,
        "COMPLETED" => Color.Info,
        "POSTPONED" => Color.Warning,
        _ => Color.Default
    };

    private bool CanManageSelectedEvent =>
        _selectedEventDetail?.HasHalLink("edit")
        ?? _selectedEvent?.HasHalLink("edit")
        ?? false;

    /// <summary>
    /// Returns the profile page URL for the given actor, or null if no public profile exists for that actor type.
    /// Organization (ActorTypeId=2) → /organization/profile/{id}.
    /// </summary>
    private static string? GetActorProfileUrl(Guid? actorId, int? actorTypeId)
    {
        if (actorId == null || actorTypeId == null) return null;
        return actorTypeId.Value switch
        {
            2 => $"/organization/profile/{actorId.Value}",  // Organization
            4 => $"/group/profile/{actorId.Value}",          // Group
            _ => null
        };
    }

    private void NavigateToActorProfile(Guid? actorId, int? actorTypeId)
    {
        var url = GetActorProfileUrl(actorId, actorTypeId);
        if (url != null)
            Navigation.NavigateTo(url);
    }

    private bool HasDetailTags()
    {
        return GetDetailTagItems().Any();
    }

    private IEnumerable<string> GetDetailTags()
    {
        return GetDetailTagItems().Select(item => item.Name);
    }

    private bool HasDetailCategories()
    {
        return GetDetailCategoryItems().Any();
    }

    private IEnumerable<string> GetDetailCategories()
    {
        return GetDetailCategoryItems().Select(item => item.Name);
    }

    // ── Event prev/next navigation ──

    private bool CanNavigatePrevEvent()
    {
        return _selectionController.CanNavigatePrevious(_selectedEvent);
    }

    private bool CanNavigateNextEvent()
    {
        return _selectionController.CanNavigateNext(_selectedEvent);
    }

    private async Task NavigatePrevEvent()
    {
        var previousEvent = _selectionController.GetPreviousEvent(_selectedEvent);
        if (previousEvent is not null)
        {
            _showTagCatPopup = false;
            await SelectEvent(previousEvent);
        }
    }

    private async Task NavigateNextEvent()
    {
        var nextEvent = _selectionController.GetNextEvent(_selectedEvent);
        if (nextEvent is not null)
        {
            _showTagCatPopup = false;
            await SelectEvent(nextEvent);
        }
    }

    // ── Tag/Category management ──

    private IEnumerable<TagCategoryManagerPopup.TagCategoryItem> GetDetailTagItems()
    {
        if (_selectedEventDetail?.Tags is { Count: > 0 })
        {
            foreach (var tag in _selectedEventDetail.Tags)
            {
                if (tag.Id.HasValue && !string.IsNullOrWhiteSpace(tag.FullName))
                {
                    yield return new TagCategoryManagerPopup.TagCategoryItem(tag.Id.Value, tag.FullName);
                }
            }

            yield break;
        }

        foreach (var item in GetTagCategoryItemsFromAdditionalProperties("tags"))
        {
            yield return item;
        }
    }

    private IEnumerable<TagCategoryManagerPopup.TagCategoryItem> GetDetailCategoryItems()
    {
        if (_selectedEventDetail?.Categories is { Count: > 0 })
        {
            foreach (var category in _selectedEventDetail.Categories)
            {
                if (category.Id.HasValue && !string.IsNullOrWhiteSpace(category.FullName))
                {
                    yield return new TagCategoryManagerPopup.TagCategoryItem(category.Id.Value, category.FullName);
                }
            }

            yield break;
        }

        foreach (var item in GetTagCategoryItemsFromAdditionalProperties("categories"))
        {
            yield return item;
        }
    }

    private IEnumerable<TagCategoryManagerPopup.TagCategoryItem> GetTagCategoryItemsFromAdditionalProperties(string propertyName)
    {
        if (_selectedEventDetail?.AdditionalProperties == null) yield break;
        if (!_selectedEventDetail.AdditionalProperties.TryGetValue(propertyName, out var val) || val is not System.Text.Json.JsonElement jsonArray) yield break;
        if (jsonArray.ValueKind != System.Text.Json.JsonValueKind.Array) yield break;

        foreach (var item in jsonArray.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) && idProp.TryGetGuid(out var g) ? g : (Guid?)null;
            var name = item.TryGetProperty("fullName", out var fn) ? fn.GetString()
                     : item.TryGetProperty("name", out var n) ? n.GetString()
                     : null;
            if (id.HasValue && !string.IsNullOrEmpty(name))
            {
                yield return new TagCategoryManagerPopup.TagCategoryItem(id.Value, name);
            }
        }
    }

    private async Task OpenTagManagement()
    {
        _tagCatMode = TagCategoryMode.Tags;
        _tagCatInitialIds = GetDetailTagItems().Select(x => x.Id).ToList().AsReadOnly();
        _showTagCatPopup = true;
        await RefreshDetailPreviewAsync();
    }

    private async Task OpenCategoryManagement()
    {
        _tagCatMode = TagCategoryMode.Categories;
        _tagCatInitialIds = GetDetailCategoryItems().Select(x => x.Id).ToList().AsReadOnly();
        _showTagCatPopup = true;
        await RefreshDetailPreviewAsync();
    }

    private async Task HandleTagCatSaved(IReadOnlyCollection<Guid> newIds)
    {
        var label = _tagCatMode == TagCategoryMode.Tags ? "Tag" : "Category";
        Snackbar.Add($"{label} changes saved.", Severity.Success);

        if (_selectedEvent?.Id != null)
        {
            try
            {
                var detail = await EventService.GetEventByIdAsync(_selectedEvent.Id.Value);
                if (detail != null)
                {
                    _selectedEventDetail = detail;
                    StateHasChanged();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing event after {Label} changes", label);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        DockLayoutState.Changed -= OnDockLayoutChanged;
        if (_dockingController is not null)
        {
            await _dockingController.DisposeAsync();
            _dockingController = null;
        }

        // Flush any pending autosave before disposing
        if (_autosaveTimer is not null)
        {
            await _autosaveTimer.DisposeAsync();
            _autosaveTimer = null;

            // Fire final save if there are pending changes
            Dictionary<string, string>? finalChanges = null;
            lock (_pendingChangesLock)
            {
                if (_pendingChanges.Count > 0)
                {
                    finalChanges = new Dictionary<string, string>(_pendingChanges);
                    _pendingChanges.Clear();
                }
            }

            if (finalChanges is not null)
            {
                try
                {
                    await UserSettingsService.UpdateSettingsBatchAsync("event-list", finalChanges);
                    UserSettingsService.InvalidateCache("event-list");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to flush pending settings on dispose");
                }
            }
        }

        try
        {
            if (_imagePreloaderModule is not null)
            {
                await _imagePreloaderModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected during navigation — safe to ignore
        }
    }

    public sealed class EventListState
    {
        public List<EventListDto> InitialItems { get; init; } = new();
        public int TotalCount { get; init; }
        public int InitialStartIndex { get; init; }
        public BrowseMode BrowseMode { get; init; } = BrowseMode.InfiniteScroll;
        public int CurrentPage { get; init; } = 1;
        public int PageSize { get; init; } = 20;
        public bool IsIslamicModuleEnabled { get; init; }
        public bool IsTechModuleEnabled { get; init; }
        public bool EventCardClickOpensDetailPage { get; init; }
        public List<EventTypeListDto> EventTypes { get; init; } = new();
        public List<AudienceGenderListDto> AudienceGenders { get; init; } = new();
        public List<AudienceAgeListDto> AudienceAges { get; init; } = new();
        public List<EventFormatListDto> EventFormats { get; init; } = new();
        public List<CategoryListDto> Categories { get; init; } = new();
        public List<TagListDto> Tags { get; init; } = new();
        public List<MadhabListDto> Madhabs { get; init; } = new();
        public List<RegistrationModeListDto> RegistrationModes { get; init; } = new();
        public List<LanguageListDto> Languages { get; init; } = new();
        public List<TagTypeWithTagsDto> TagGroups { get; init; } = new();
        public List<CategoryTypeWithCategoriesDto> CategoryGroups { get; init; } = new();
    }
}
