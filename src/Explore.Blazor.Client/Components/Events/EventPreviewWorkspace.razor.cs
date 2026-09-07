using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Events;

public partial class EventPreviewWorkspace : ComponentBase, IDisposable
{
    private const int ModeratedStatusId = 6;

    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IEventService EventService { get; set; } = default!;
    [Inject] private Explore.Blazor.Client.Contracts.Services.IEventSessionService EventSessionService { get; set; } = default!;
    [Inject] private IPublicExperienceService PublicExperienceService { get; set; } = default!;
    [Inject] private DockLayoutState DockLayoutState { get; set; } = default!;
    [Inject] private IBrowserActionInterop BrowserActionInterop { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;
    [Inject] private ILogger<EventPreviewWorkspace> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;
    [Parameter] public IEnumerable<EventListDto> NavigationEvents { get; set; } = [];
    [Parameter] public EventCallback<EventListDto> OnEventDeleted { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public string DataTestId { get; set; } = "event-preview-workspace";
    [Parameter] public string ContentDataTestId { get; set; } = "event-preview-workspace-content";

    public Guid? SelectedEventId => _detailDrawerOpen ? _selectedEvent?.Id : null;

    private EventListDto? _selectedEvent;
    private EventDto? _selectedEventDetail;
    private ICollection<EventSessionListDto>? _selectedEventSessions;
    private bool _detailDrawerOpen;
    private bool _isLoadingDetail;
    private bool _detailImageLoadFailed;
    private bool _isDetailImageLoading;
    private bool _eventCardClickOpensDetailPage;
    private bool _panelRegistered;
    private bool _showTagCatPopup;
    private TagCategoryMode _tagCatMode;
    private IReadOnlyCollection<Guid> _tagCatInitialIds = Array.Empty<Guid>();

    private string RootClass => string.Join(' ', new[]
    {
        "event-preview-workspace",
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string SelectedEventPageUrl => EventUrlHelper.BuildPublicPath(_selectedEvent?.Slug, _selectedEvent?.PublicCode)
        ?? (_selectedEvent?.Id is Guid eventId && eventId != Guid.Empty
            ? $"/events/{eventId}"
            : "/events");

    private string SelectedDescription => _selectedEventDetail?.Description
        ?? _selectedEvent?.Description
        ?? string.Empty;

    private bool HasDetailActualImage => _selectedEvent is not null
        && HasUsableFeaturedImage(_selectedEvent)
        && !_detailImageLoadFailed;

    private bool ShouldShowDetailImageSkeleton => _isLoadingDetail || _isDetailImageLoading;

    private string DetailImageSrc => _selectedEvent is null
        ? string.Empty
        : _detailImageLoadFailed || string.IsNullOrWhiteSpace(_selectedEvent.FeaturedImageUri)
            ? GetFallbackEventImage(_selectedEvent)
            : _selectedEvent.FeaturedImageUri;

    private string DetailImageCssClass => string.Join(' ', new[]
    {
        HasDetailActualImage
            ? "event-preview-workspace__image event-preview-workspace__image--actual"
            : "event-preview-workspace__image event-preview-workspace__image--fallback",
        ShouldShowDetailImageSkeleton ? "event-preview-workspace__image--loading" : null
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string ProgramCountText => _selectedEventDetail is null
        ? GetProgramCountText(_selectedEvent?.SessionCount ?? _selectedEventSessions?.Count ?? 0)
        : GetProgramCountText(_selectedEventDetail.SessionCount ?? _selectedEventSessions?.Count ?? 0);

    private string RegistrationPolicyText => !string.IsNullOrWhiteSpace(_selectedEventDetail?.RegistrationPolicyFullName)
        ? _selectedEventDetail.RegistrationPolicyFullName
        : !string.IsNullOrWhiteSpace(_selectedEvent?.RegistrationPolicyFullName)
            ? _selectedEvent.RegistrationPolicyFullName!
            : _selectedEventDetail?.ParticipationConfiguration?.AdvanceRegistrationObligationName
                ?? _selectedEventDetail?.ParticipationConfiguration?.ParticipationHandlingModeName
                ?? _selectedEvent?.ParticipationConfiguration?.AdvanceRegistrationObligationName
                ?? _selectedEvent?.ParticipationConfiguration?.ParticipationHandlingModeName
                ?? "Participation details not specified";

    private IEnumerable<string> DetailTags => _selectedEventDetail?.Tags?
        .Where(tag => !string.IsNullOrWhiteSpace(tag.FullName))
        .Select(tag => tag.FullName!)
        ?? [];

    private IEnumerable<string> DetailCategories => _selectedEventDetail?.Categories?
        .Where(category => !string.IsNullOrWhiteSpace(category.FullName))
        .Select(category => category.FullName!)
        ?? [];

    private bool CanEditSelectedEvent => _selectedEvent?.HasHalLink("edit") == true
        || _selectedEventDetail?.HasHalLink("edit") == true;

    private bool CanDeleteSelectedEvent => _selectedEvent?.HasHalLink("delete") == true
        || _selectedEventDetail?.HasHalLink("delete") == true;

    private bool CanManageSelectedEvent => CanEditSelectedEvent || CanDeleteSelectedEvent;

    private bool CanNavigatePreviousEvent => GetAdjacentEvent(-1) is not null;
    private bool CanNavigateNextEvent => GetAdjacentEvent(1) is not null;
    protected override async Task OnInitializedAsync()
    {
        RegisterPreviewPanel();
        DockLayoutState.Changed += OnDockLayoutChanged;

        await LoadClickBehaviorAsync();
    }

    public async Task SelectEventAsync(EventListDto evt)
    {
        if (evt.Id is not Guid eventId || eventId == Guid.Empty)
        {
            Snackbar.Add("This event cannot be opened yet.", Severity.Warning);
            return;
        }

        if (_eventCardClickOpensDetailPage && !IsModeratedEvent(evt))
        {
            var path = EventUrlHelper.BuildPublicPath(evt.Slug, evt.PublicCode) ?? $"/events/{eventId}";
            Navigation.NavigateTo(path);
            return;
        }

        _selectedEvent = evt;
        _selectedEventDetail = null;
        _selectedEventSessions = null;
        _showTagCatPopup = false;
        _detailImageLoadFailed = false;
        _isDetailImageLoading = HasUsableFeaturedImage(evt);
        _detailDrawerOpen = true;
        _isLoadingDetail = true;

        RegisterPreviewPanel();
        DockLayoutState.Open(EventDockPanels.EventPreviewId);

        try
        {
            var detailTask = EventService.GetEventByIdAsync(eventId) ?? Task.FromResult<EventDto?>(null);
            var sessionsTask = EventSessionService.GetSessionsByEventAsync(eventId) ?? Task.FromResult<ICollection<EventSessionListDto>>([]);

            await Task.WhenAll(detailTask, sessionsTask);

            _selectedEventDetail = await detailTask;
            _selectedEventSessions = await sessionsTask;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to load event preview details for event {EventId}", eventId);
            Snackbar.Add("Opened preview with limited event details.", Severity.Info);
        }
        finally
        {
            _isLoadingDetail = false;
            await RefreshDetailPreviewAsync();
        }
    }

    public void NavigateToEdit(EventListDto evt)
    {
        if (evt.Id is Guid eventId && eventId != Guid.Empty && evt.HasHalLink("edit"))
        {
            Navigation.NavigateTo($"/events/{eventId}/edit");
        }
    }

    public async Task OpenDeleteDialogAsync(EventListDto evt)
    {
        if (evt.Id is not Guid eventId || eventId == Guid.Empty || !evt.HasHalLink("delete"))
        {
            return;
        }

        var parameters = new DialogParameters
        {
            ["EventId"] = eventId,
            ["EventTitle"] = evt.Title
        };

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await DeleteEventDialog.ShowAsync(DialogService, "Delete Event", parameters, DialogOptionsFactory.Small());
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result is null || result.Canceled)
        {
            return;
        }

        if (_selectedEvent?.Id == eventId)
        {
            await CloseDetailPanelAsync();
        }

        if (OnEventDeleted.HasDelegate)
        {
            await OnEventDeleted.InvokeAsync(evt);
        }
    }

    public async Task ShareEventAsync(EventListDto eventToShare)
    {
        if (eventToShare.Id is not Guid eventId || eventId == Guid.Empty)
        {
            Snackbar.Add("Sharing is unavailable for this event.", Severity.Warning);
            return;
        }

        if (IsModeratedEvent(eventToShare))
        {
            Snackbar.Add("Sharing is unavailable for moderated events.", Severity.Warning);
            return;
        }

        var path = EventUrlHelper.BuildPublicPath(eventToShare.Slug, eventToShare.PublicCode) ?? $"/events/{eventId}";
        var url = CanonicalUrlHelper.Build(Navigation, path);

        if (await BrowserActionInterop.ShareAsync(eventToShare.Title ?? "Event", url))
        {
            return;
        }

        await CopyEventLinkToClipboardAsync(url);
    }

    private async Task LoadClickBehaviorAsync()
    {
        try
        {
            var settings = await PublicExperienceService.GetSettingsAsync();
            _eventCardClickOpensDetailPage = settings?.EventCardClickOpensDetailPage ?? false;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to load event-card click behavior settings; using sidebar preview fallback.");
            _eventCardClickOpensDetailPage = false;
        }
    }

    private void RegisterPreviewPanel()
    {
        if (_panelRegistered)
        {
            return;
        }

        if (DockLayoutState.GetPanel(EventDockPanels.EventPreviewId) is not null)
        {
            DockLayoutState.Unregister(EventDockPanels.EventPreviewId);
        }

        DockLayoutState.Register(EventDockPanels.EventPreview, RenderEventPreviewPanel);
        _panelRegistered = true;
    }

    private async Task CloseDetailPanelAsync()
    {
        _detailDrawerOpen = false;
        ClearDetailPreview();

        if (DockLayoutState.GetPanel(EventDockPanels.EventPreviewId) is not null)
        {
            DockLayoutState.Close(EventDockPanels.EventPreviewId);
        }

        await RefreshDetailPreviewAsync();
    }

    private void ClearDetailPreview()
    {
        _selectedEvent = null;
        _selectedEventDetail = null;
        _selectedEventSessions = null;
        _detailImageLoadFailed = false;
        _isDetailImageLoading = false;
        _isLoadingDetail = false;
        _showTagCatPopup = false;
    }

    private async Task NavigatePreviousEventAsync()
    {
        var previous = GetAdjacentEvent(-1);
        if (previous is not null)
        {
            _showTagCatPopup = false;
            await SelectEventAsync(previous);
        }
    }

    private async Task NavigateNextEventAsync()
    {
        var next = GetAdjacentEvent(1);
        if (next is not null)
        {
            _showTagCatPopup = false;
            await SelectEventAsync(next);
        }
    }

    private EventListDto? GetAdjacentEvent(int offset)
    {
        if (_selectedEvent?.Id is not Guid selectedId)
        {
            return null;
        }

        var events = NavigationEvents
            .Where(evt => evt.Id.HasValue)
            .ToList();
        var currentIndex = events.FindIndex(evt => evt.Id == selectedId);
        var adjacentIndex = currentIndex + offset;

        return currentIndex >= 0 && adjacentIndex >= 0 && adjacentIndex < events.Count
            ? events[adjacentIndex]
            : null;
    }

    private void NavigateToSelectedEventPage()
    {
        Navigation.NavigateTo(SelectedEventPageUrl);
    }

    private void NavigateToSelectedEventEdit()
    {
        if (_selectedEvent is not null)
        {
            NavigateToEdit(_selectedEvent);
        }
    }

    private async Task DeleteSelectedEventAsync()
    {
        if (_selectedEvent is not null)
        {
            await OpenDeleteDialogAsync(_selectedEvent);
        }
    }

    private async Task ShareSelectedEventAsync()
    {
        if (_selectedEvent is not null)
        {
            await ShareEventAsync(_selectedEvent);
        }
    }

    private async Task CopySelectedEventLinkAsync()
    {
        if (_selectedEvent?.Id is not Guid eventId || eventId == Guid.Empty || IsModeratedEvent(_selectedEvent))
        {
            return;
        }

        var path = EventUrlHelper.BuildPublicPath(_selectedEvent?.Slug, _selectedEvent?.PublicCode) ?? $"/events/{eventId}";
        var url = CanonicalUrlHelper.Build(Navigation, path);
        await CopyEventLinkToClipboardAsync(url);
    }

    private async Task CopyEventLinkToClipboardAsync(string url)
    {
        if (await BrowserActionInterop.CopyTextAsync(url))
        {
            Snackbar.Add("Link copied to clipboard", Severity.Success, options => options.VisibleStateDuration = 2000);
            return;
        }

        Logger.LogWarning("Failed to copy event link to clipboard");
        Snackbar.Add("Could not copy link", Severity.Error);
    }

    private async Task HandleDetailImageLoadedAsync()
    {
        if (!_isDetailImageLoading && !_isLoadingDetail)
        {
            return;
        }

        _isDetailImageLoading = false;
        await RefreshDetailPreviewAsync();
    }

    private async Task HandleDetailImageErrorAsync()
    {
        _detailImageLoadFailed = true;
        _isDetailImageLoading = false;
        await RefreshDetailPreviewAsync();
    }

    private async Task RefreshDetailPreviewAsync()
    {
        await InvokeAsync(StateHasChanged);
        DockLayoutState.Refresh();
    }

    private async Task SetTagCategoryPopupVisibleAsync(bool value)
    {
        _showTagCatPopup = value;
        await RefreshDetailPreviewAsync();
    }

    private async Task OpenTagManagementAsync()
    {
        _tagCatMode = TagCategoryMode.Tags;
        _tagCatInitialIds = GetDetailTagItems().Select(x => x.Id).ToList().AsReadOnly();
        _showTagCatPopup = true;
        await RefreshDetailPreviewAsync();
    }

    private async Task OpenCategoryManagementAsync()
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

        if (_selectedEvent?.Id is not Guid eventId)
        {
            return;
        }

        try
        {
            var detail = await EventService.GetEventByIdAsync(eventId);
            if (detail is not null)
            {
                _selectedEventDetail = detail;
                await RefreshDetailPreviewAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing event after {Label} changes", label);
        }
    }

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
        }
    }

    private void NavigateToSelectedActorProfile()
    {
        if (_selectedEvent is null)
        {
            return;
        }

        var url = GetActorProfileUrl(_selectedEvent.ActorId, _selectedEvent.ActorTypeId);
        if (url is not null)
        {
            Navigation.NavigateTo(url);
        }
    }

    private void OnDockLayoutChanged()
    {
        if (!_detailDrawerOpen)
        {
            return;
        }

        var previewOpen = DockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen == true;
        if (previewOpen)
        {
            return;
        }

        _detailDrawerOpen = false;
        ClearDetailPreview();
        _ = InvokeAsync(StateHasChanged);
    }

    private static string GetEventTypeName(EventListDto eventItem)
        => !string.IsNullOrWhiteSpace(eventItem.EventTypeFullName) ? eventItem.EventTypeFullName : "Event";

    private static string GetLocationText(EventListDto eventItem)
    {
        if (eventItem.EventFormatId == 2) return "Online";
        if (!string.IsNullOrWhiteSpace(eventItem.EventFormatFullName)) return eventItem.EventFormatFullName;
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

    private static bool IsModeratedEvent(EventListDto? eventItem) =>
        eventItem?.EventStatusId == ModeratedStatusId ||
        string.Equals(eventItem?.EventStatusFullName, "Moderated", StringComparison.OrdinalIgnoreCase);

    private static string FormatEventDate(EventListDto eventItem)
    {
        if (eventItem.FirstSessionDate is null)
        {
            return "Date TBD";
        }

        var start = eventItem.FirstSessionDate.Value;
        if (eventItem.LastSessionDate is { } end && end.Date != start.Date)
        {
            return $"{start:MMM dd} — {end:MMM dd, yyyy}";
        }

        return start.ToString("MMM dd, yyyy · h:mm tt");
    }

    private static string FormatSessionTime(EventSessionListDto session)
    {
        if (session.StartTime is null)
        {
            return session.EventSessionKindFullName ?? "Time TBD";
        }

        var text = session.StartTime.Value.ToString("MMM dd · h:mm tt");
        if (session.EndTime is { } endTime)
        {
            text += $"–{endTime:h:mm tt}";
        }

        return text;
    }

    private static string GetProgramCountText(int count) => count switch
    {
        <= 0 => "Program not published yet",
        1 => "1 program item",
        _ => $"{count} program items"
    };

    private static Color GetStatusColor(string? masterCode) => masterCode switch
    {
        "PUBLISHED" => Color.Success,
        "DRAFT" => Color.Default,
        "CANCELLED" => Color.Error,
        "COMPLETED" => Color.Info,
        "POSTPONED" => Color.Warning,
        _ => Color.Default
    };

    private string GetFallbackEventImage(EventListDto eventItem)
        => ImageHelper.GetEventImageUrl(null, eventItem.Title, GetEventColor(eventItem), width: 300, height: 400);

    private static string GetEventColor(EventListDto eventItem)
    {
        var color = EventColorHelper.GetColorByTypeId(eventItem.EventTypeId);
        return color != EventColorHelper.DefaultColor ? color : EventColorHelper.GetColorByHash(eventItem.Title);
    }

    private static bool HasUsableFeaturedImage(EventListDto eventItem)
        => !string.IsNullOrWhiteSpace(eventItem.FeaturedImageUri);

    private static string GetActorInitials(string? displayName)
        => DisplayHelper.GetInitials(displayName);

    private static string? GetActorProfileUrl(Guid? actorId, int? actorTypeId)
    {
        if (actorId is null || actorTypeId is null)
        {
            return null;
        }

        return actorTypeId.Value switch
        {
            2 => $"/organization/profile/{actorId.Value}",
            4 => $"/group/profile/{actorId.Value}",
            _ => null
        };
    }

    public void Dispose()
    {
        DockLayoutState.Changed -= OnDockLayoutChanged;

        if (_panelRegistered && DockLayoutState.GetPanel(EventDockPanels.EventPreviewId) is not null)
        {
            DockLayoutState.Unregister(EventDockPanels.EventPreviewId);
        }
    }
}
