using System.Text.Json;
using System.Text.Json.Serialization;
using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.EventReporting;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events;

/// <summary>
/// Code-behind for EventDetail page.
/// Displays detailed information about an event including sessions, registration, and organizer info.
/// </summary>
public partial class EventDetail : ComponentBase, IDisposable
{
    private const string MainContentAppearanceOwner = "EventDetail";
    private const string EventDetailMainContentStyle = "--layout-padding-inline: 0px;";
    private const string DraftStatusMasterCode = "DRAFT";
    private const string PublishedStatusMasterCode = "PUBLISHED";
    private const string CancelledStatusMasterCode = "CANCELLED";
    private const string CompletedStatusMasterCode = "COMPLETED";
    private const string ModeratedStatusMasterCode = "MODERATED";
    private const string PublicVisibilityMasterCode = "PUBLIC";
    private const string SchemaEventScheduled = "https://schema.org/EventScheduled";
    private const string SchemaEventCancelled = "https://schema.org/EventCancelled";
    private const string DefaultBrandDisplayName = "Event Platform";
    private const string EditLinkRelation = "edit";
    private const string DeleteLinkRelation = "delete";
    private const string TeamLinkRelation = "team";
    private const string PublishLinkRelation = "publish";
    private const string CancelLinkRelation = "cancel";
    private const string ModerateLightLinkRelation = "moderate-light";
    private const string ModerateHeavyLinkRelation = "moderate-heavy";
    private const string UnmoderateLinkRelation = "unmoderate";
    private const string ArchiveLinkRelation = "archive";
    private const string AddSessionLinkRelation = "add-session";
    private const string AddSessionGroupLinkRelation = "add-session-group";
    private const string ModerationHistoryLinkRelation = "moderation-history";
    private const string ModerationReportsLinkRelation = "moderation-reports";
    private const string ReportEventLinkRelation = "report-event";
    private const string ReportIntentQueryParameter = "report";
    private const string ReportIntentQueryValue = "1";
    private const string ReportLoginPromptTitle = "Need to report this?";
    private const string ReportLoginPromptMessage = "Sign in to report content that breaks our rules. You can also file a legal complaint without signing in.";

    private static readonly JsonSerializerOptions EventStructuredDataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> PhysicalLocationExtensionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "locationId",
        "physicalLocationId",
        "locationName",
        "locationFullName",
        "locationAddress",
        "address",
        "postcode",
        "postalCode",
        "locationCity",
        "city",
        "locationCountry",
        "country",
        "latitude",
        "longitude",
        "coordinates",
        "roomId",
        "roomName"
    };

    private static readonly IReadOnlyList<EventModerationReasonOption> LightModerationReasonOptions =
    [
        new("policy_review", "Policy review"),
        new("community_safety_review", "Community safety review"),
        new("event_details_need_review", "Event details need review")
    ];

    private static readonly IReadOnlyList<EventModerationReasonOption> HeavyModerationReasonOptions =
    [
        new("illegal_content", "Illegal content"),
        new("illegal_image", "Illegal image"),
        new("severe_safety_violation", "Severe safety violation")
    ];

    private static readonly IReadOnlyList<EventModerationReasonOption> UnmoderationReasonOptions =
    [
        new("review_cleared", "Review cleared"),
        new("appeal_approved", "Appeal approved"),
        new("policy_exception_approved", "Policy exception approved")
    ];

    [Inject] private MainContentAppearanceState MainContentAppearanceState { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private TimeProvider Clock { get; set; } = TimeProvider.System;
    [Inject] private IEventService EventService { get; set; } = default!;
    [Inject] private Explore.Blazor.Client.Contracts.Services.IEventSessionService EventSessionService { get; set; } = default!;
    [Inject] private Explore.Blazor.Client.Contracts.Services.IEventModerationService EventModerationService { get; set; } = default!;
    [Inject] private IPublicExperienceService PublicExperienceService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IMapsService MapsService { get; set; } = default!;
    [Inject] private RouterStateService RouterState { get; set; } = default!;
    [Inject] private IUserService UserService { get; set; } = default!;
    [Inject] private Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private IEventAspectService EventAspectService { get; set; } = default!;
    [Inject] private IEventSessionAgendaItemService AgendaItemService { get; set; } = default!;
    [Inject] private IEventAgendaItemService EventAgendaItemService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;
    [Inject] private IBrowserActionInterop BrowserActionInterop { get; set; } = default!;
    [Inject] private ITranslationService Translation { get; set; } = default!;

    [PersistentState]
    public EventDetailState? PersistedState { get; set; }

    [SupplyParameterFromQuery(Name = ReportIntentQueryParameter)]
    public string? ReportIntent { get; set; }

    // Changed from private to protected to be accessible by Razor view
    [Inject] protected ILogger<EventDetail> Logger { get; set; } = default!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AnnouncerService { get; set; } = default!;
    [Inject] private IEventDayService EventDayService { get; set; } = default!;
    [Inject] private IEventLocationService EventLocationService { get; set; } = default!;

    private Guid EventId { get; set; }

    private EventDto? _eventDetails;
    private ICollection<EventSessionListDto>? _eventSessions;
    private EventSessionListDto? _primarySession;
    private bool _isLoading = true;
    private bool _canDelete = false;
    private bool _canEdit = false;
    private bool _canManageTeam = false;
    private bool _canPublish = false;
    private bool _canCancel = false;
    private bool _canModerateLight = false;
    private bool _canModerateHeavy = false;
    private bool _canUnmoderate = false;
    private bool _canArchive = false;
    private bool _canReport = false;
    private bool _canViewModerationReports = false;
    private bool _isProcessingEventAction = false;
    private bool _isAuthenticated = false;
    private bool _isCheckingAuth = true;

    // Kept apart on purpose: structured data may only ever read the public projection, while the visible
    // card may layer the attendee projection on top.
    private EventLocationDisclosureView? _publicLocationView;
    private EventLocationDisclosureView? _attendeeLocationView;

    private string? _errorMessage;
    private bool _imageLoadFailed;
    private Guid _lastRenderedEventId;
    private bool _wasLoading = true;
    private bool _hasHandledReportIntent;
    private string _brandDisplayName = DefaultBrandDisplayName;
    private TenantDirectoryOperatorPublicDto? _directoryOperator;

    private ICollection<EventDayListDto>? _eventDays;

    // Event Aspects
    private EventIslamicAspectDto? _islamicAspect;
    private EventTechAspectDto? _techAspect;
    private ICollection<EventSessionAgendaItemListDto>? _agendaItems;
    private ICollection<EventAgendaItemListDto>? _eventAgendaItems;
    private AppearanceSettings _appearance = new();

    // Tag/Category management
    private bool _showDetailTagCatPopup;
    private TagCategoryMode _detailTagCatMode;
    private IReadOnlyCollection<Guid> _detailTagCatInitialIds = Array.Empty<Guid>();

    private bool HasManagementTopBar =>
        _eventDetails != null &&
        !_isCheckingAuth &&
        (_canEdit || _canViewModerationReports || _canPublish || _canCancel || _canModerateLight || _canModerateHeavy || _canUnmoderate || _canArchive);

    private bool CanRequestManagedSessions =>
        _eventDetails is not null &&
        (_canEdit ||
         _canManageTeam ||
         _canPublish ||
         _canCancel ||
         _canModerateLight ||
         _canModerateHeavy ||
         _canUnmoderate ||
         _canArchive ||
         _eventDetails.HasHalLink(AddSessionLinkRelation) ||
         _eventDetails.HasHalLink(AddSessionGroupLinkRelation) ||
         _eventDetails.HasHalLink(ModerationHistoryLinkRelation) ||
         _eventDetails.HasHalLink(ModerationReportsLinkRelation));

    private bool HasMultipleSessions => _eventSessions?.Count > 1;
    private string BrandDisplayName => _brandDisplayName;
    private bool IsPaidEvent =>
        _eventDetails?.TicketPriceSummary is { } summary &&
        !string.Equals(summary.SummaryCode, "FREE", StringComparison.OrdinalIgnoreCase);
    private bool ShouldShowPaidEventDirectoryOperator => _directoryOperator is not null && IsPaidEvent;
    private bool PaidRegistrationIdentityUnavailable => IsPaidEvent && _directoryOperator is null;

    /// <summary>
    /// Initializes the component and loads event data.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        var slugCode = RouterState.GetParam("slugCode");
        await LoadBrandingAsync();
        await LoadEventDataAsync(slugCode);
    }

    private async Task LoadBrandingAsync()
    {
        try
        {
            var shell = await PublicExperienceService.GetCachedShellAsync();
            _brandDisplayName = NormalizeBrandDisplayName(shell?.Home?.BrandDisplayName);
            _directoryOperator = shell?.DirectoryOperator;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Unable to load public brand display name for event metadata");
            _brandDisplayName = DefaultBrandDisplayName;
            _directoryOperator = null;
        }
    }

    /// <summary>
    /// Resets scroll position when navigating to a new event.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (_lastRenderedEventId != EventId || (_wasLoading && !_isLoading))
        {
            _lastRenderedEventId = EventId;
            _wasLoading = _isLoading;
            try
            {
                await JsRuntime.InvokeVoidAsync("window.scrollTo", 0, 0);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to scroll to top on navigation");
            }
        }

        if (!_isLoading && _eventDetails is not null)
        {
            await TryOpenPendingReportDialogAsync();
        }
    }

    /// <summary>
    /// Loads event details, sessions, and registration status.
    /// </summary>
    private async Task LoadEventDataAsync(string? slugCode = null)
    {
        _isLoading = true;
        _isCheckingAuth = true;
        _errorMessage = null;
        _imageLoadFailed = false;

        try
        {
            Logger.LogInformation("Loading event {SlugCode}", slugCode);
            _eventDetails = string.IsNullOrWhiteSpace(slugCode)
                ? await EventService.GetEventByIdAsync(EventId)
                : await EventService.GetEventBySlugCodeAsync(slugCode);

            if (_eventDetails != null)
            {
                RemovePhysicalLocationExtensionData(_eventDetails.AdditionalProperties);

                if (_eventDetails.Id is not Guid loadedEventId || loadedEventId == Guid.Empty)
                {
                    _errorMessage = "Event not found";
                    return;
                }

                EventId = loadedEventId;
                Logger.LogInformation("Loaded event: {Title}", _eventDetails.Title);
                _appearance = new AppearanceSettings
                {
                    BackgroundColor = _eventDetails.BackgroundColor ?? string.Empty,
                    ImageUri = _eventDetails.BackgroundImageUri ?? string.Empty,
                    BackgroundEffect = _eventDetails.BackgroundEffect ?? "None"
                };
                PublishMainContentAppearance();
                _islamicAspect = MapIslamicAspect(_eventDetails.IslamicAspect);
                _techAspect = MapTechAspect(_eventDetails.TechAspect);

                CheckAuthorizationFromHalLinks();

                await LoadEventLocationDisclosureAsync();

                _eventSessions = RemovePhysicalLocationData(await EventSessionService.GetSessionsByEventAsync(
                    EventId,
                    includeManagedSessions: CanRequestManagedSessions));
                _primarySession = _eventSessions?.FirstOrDefault();
                Logger.LogInformation("Loaded {SessionCount} sessions", _eventSessions?.Count ?? 0);

                var aspectsTask = NeedsAspectFallbackLoad()
                    ? LoadEventAspectsAsync()
                    : Task.CompletedTask;
                var daysTask = EventDayService.GetDaysByEventAsync(EventId, CanRequestManagedSessions);
                var eventAgendaTask = EventAgendaItemService.GetAgendaItemsByEventAsync(EventId);
                var agendaTask = _primarySession?.Id != null && _primarySession.Id != Guid.Empty
                    ? AgendaItemService.GetAgendaItemsBySessionAsync(_primarySession.Id.Value)
                    : Task.FromResult<ICollection<EventSessionAgendaItemListDto>>(new List<EventSessionAgendaItemListDto>());
                await Task.WhenAll(aspectsTask, daysTask, eventAgendaTask, agendaTask);
                _agendaItems = RemovePhysicalLocationData(await agendaTask);
                _eventDays = await daysTask;
                _eventAgendaItems = await eventAgendaTask;
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Failed to load event details: {ex.Message}";
            Logger.LogError(ex, "Failed to load event {EventId}", EventId);
        }
        finally
        {
            _isLoading = false;
            _isCheckingAuth = false;
        }

        if (_errorMessage != null)
            await AnnouncerService.AnnounceAssertiveAsync(_errorMessage);
        else if (_eventDetails == null)
            await AnnouncerService.AnnouncePoliteAsync("Event not found");

        if (_eventDetails != null)
        {
            PersistState();
        }
    }

    private bool TryRestoreState()
    {
        if (PersistedState == null || PersistedState.EventId != EventId)
        {
            return false;
        }

        _eventDetails = PersistedState.EventDetails;
        RemovePhysicalLocationExtensionData(_eventDetails?.AdditionalProperties);
        _eventSessions = RemovePhysicalLocationData(PersistedState.EventSessions.ToList());
        _primarySession = _eventSessions.FirstOrDefault();
        _islamicAspect = PersistedState.IslamicAspect;
        _techAspect = PersistedState.TechAspect;
        _eventDays = PersistedState.EventDays.ToList();
        _eventAgendaItems = PersistedState.EventAgendaItems.ToList();
        _agendaItems = RemovePhysicalLocationData(PersistedState.SessionAgendaItems.ToList());
        _appearance = PersistedState.Appearance ?? new AppearanceSettings();
        _imageLoadFailed = false;
        PublishMainContentAppearance();
        _isLoading = false;
        _isCheckingAuth = true;

        CheckAuthorizationFromHalLinks();
        _isCheckingAuth = false;

        _ = InvokeAsync(async () =>
        {
            await RefreshRestoredEventDetailsAsync();
            StateHasChanged();
        });

        return true;
    }

    private async Task RefreshRestoredEventDetailsAsync()
    {
        try
        {
            var refreshedEvent = await EventService.GetEventByIdAsync(EventId);
            if (refreshedEvent == null)
                return;

            _eventDetails = refreshedEvent;
            RemovePhysicalLocationExtensionData(_eventDetails.AdditionalProperties);
            _appearance = new AppearanceSettings
            {
                BackgroundColor = _eventDetails.BackgroundColor ?? string.Empty,
                ImageUri = _eventDetails.BackgroundImageUri ?? string.Empty,
                BackgroundEffect = _eventDetails.BackgroundEffect ?? "None"
            };

            PublishMainContentAppearance();
            CheckAuthorizationFromHalLinks();
            PersistState();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to refresh event {EventId} after prerender state restore", EventId);
        }
    }

    private void PersistState()
    {
        PersistedState = new EventDetailState
        {
            EventId = EventId,
            EventDetails = _eventDetails,
            EventSessions = _eventSessions?.ToList() ?? new List<EventSessionListDto>(),
            PrimarySession = _primarySession,
            IslamicAspect = _islamicAspect,
            TechAspect = _techAspect,
            EventDays = _eventDays?.ToList() ?? new List<EventDayListDto>(),
            EventAgendaItems = _eventAgendaItems?.ToList() ?? new List<EventAgendaItemListDto>(),
            SessionAgendaItems = _agendaItems?.ToList() ?? new List<EventSessionAgendaItemListDto>(),
            Appearance = _appearance
        };
    }

    public sealed record EventDetailState
    {
        private IReadOnlyList<EventSessionListDto> _eventSessions = Array.Empty<EventSessionListDto>();
        private IReadOnlyList<EventDayListDto> _eventDays = Array.Empty<EventDayListDto>();
        private IReadOnlyList<EventAgendaItemListDto> _eventAgendaItems = Array.Empty<EventAgendaItemListDto>();
        private IReadOnlyList<EventSessionAgendaItemListDto> _sessionAgendaItems = Array.Empty<EventSessionAgendaItemListDto>();

        public Guid EventId { get; init; }
        public EventDto? EventDetails { get; init; }
        public IReadOnlyList<EventSessionListDto> EventSessions
        {
            get => _eventSessions;
            init => _eventSessions = Array.AsReadOnly(value.ToArray());
        }
        public EventSessionListDto? PrimarySession { get; init; }
        public EventIslamicAspectDto? IslamicAspect { get; init; }
        public EventTechAspectDto? TechAspect { get; init; }
        public IReadOnlyList<EventDayListDto> EventDays
        {
            get => _eventDays;
            init => _eventDays = Array.AsReadOnly(value.ToArray());
        }
        public IReadOnlyList<EventAgendaItemListDto> EventAgendaItems
        {
            get => _eventAgendaItems;
            init => _eventAgendaItems = Array.AsReadOnly(value.ToArray());
        }
        public IReadOnlyList<EventSessionAgendaItemListDto> SessionAgendaItems
        {
            get => _sessionAgendaItems;
            init => _sessionAgendaItems = Array.AsReadOnly(value.ToArray());
        }
        public AppearanceSettings? Appearance { get; init; }
    }

    private static EventIslamicAspectDto? MapIslamicAspect(EventIslamicAspectDto? aspect) => aspect;

    private static EventTechAspectDto? MapTechAspect(EventTechAspectDto? aspect) => aspect;

    private bool NeedsAspectFallbackLoad()
    {
        return ShouldLoadIslamicAspectFallback() || ShouldLoadTechAspectFallback();
    }

    private bool ShouldLoadIslamicAspectFallback() =>
        _islamicAspect is null && HasAvailableAspect("Islamic");

    private bool ShouldLoadTechAspectFallback() =>
        _techAspect is null && HasAvailableAspect("Tech");

    private bool HasAvailableAspect(string aspectName) =>
        _eventDetails?.AvailableAspects?.Any(aspect =>
            string.Equals(aspect, aspectName, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Loads event aspects (Islamic and Tech) for the current event.
    /// </summary>
    private async Task LoadEventAspectsAsync()
    {
        if (ShouldLoadIslamicAspectFallback())
        {
            try
            {
                _islamicAspect = await EventAspectService.GetIslamicAspectAsync(EventId, CanRequestManagedSessions);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading Islamic aspect for event {EventId}", EventId);
            }
        }

        if (ShouldLoadTechAspectFallback())
        {
            try
            {
                _techAspect = await EventAspectService.GetTechAspectAsync(EventId, CanRequestManagedSessions);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading tech aspect for event {EventId}", EventId);
            }
        }

        Logger.LogDebug("Loaded aspects for event {EventId}: Islamic={HasIslamic}, Tech={HasTech}",
            EventId, _islamicAspect != null, _techAspect != null);
    }

    private async Task LoadEventAgendaAsync()
    {
        try
        {
            var daysTask = EventDayService.GetDaysByEventAsync(EventId, CanRequestManagedSessions);
            var itemsTask = EventAgendaItemService.GetAgendaItemsByEventAsync(EventId);
            await Task.WhenAll(daysTask, itemsTask);
            _eventDays = await daysTask;
            _eventAgendaItems = await itemsTask;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reloading event agenda for event {EventId}", EventId);
        }

        StateHasChanged();
    }

    /// <summary>
    /// Checks if the current user is authorized to edit/delete this event
    /// by reading HAL links from the server response.
    /// </summary>
    private void CheckAuthorizationFromHalLinks()
    {
        if (_eventDetails == null)
        {
            _canEdit = false;
            _canDelete = false;
            _canManageTeam = false;
            _canPublish = false;
            _canCancel = false;
            _canModerateLight = false;
            _canModerateHeavy = false;
            _canUnmoderate = false;
            _canArchive = false;
            _canReport = false;
            _canViewModerationReports = false;
            return;
        }

        _canEdit = _eventDetails.HasHalLink(EditLinkRelation);
        _canDelete = _eventDetails.HasHalLink(DeleteLinkRelation);
        _canManageTeam = _eventDetails.HasHalLink(TeamLinkRelation);
        _canPublish = _eventDetails.HasHalLink(PublishLinkRelation);
        _canCancel = _eventDetails.HasHalLink(CancelLinkRelation);
        _canModerateLight = _eventDetails.HasHalLink(ModerateLightLinkRelation);
        _canModerateHeavy = _eventDetails.HasHalLink(ModerateHeavyLinkRelation);
        _canUnmoderate = _eventDetails.HasHalLink(UnmoderateLinkRelation);
        _canArchive = _eventDetails.HasHalLink(ArchiveLinkRelation);
        _canReport = _eventDetails.HasHalLink(ReportEventLinkRelation);
        _canViewModerationReports = _eventDetails.HasHalLink(ModerationReportsLinkRelation);
        Logger.LogDebug(
            "HAL link authorization for event {EventId}: CanEdit={CanEdit}, CanDelete={CanDelete}, CanManageTeam={CanManageTeam}, CanPublish={CanPublish}, CanCancel={CanCancel}, CanModerateLight={CanModerateLight}, CanModerateHeavy={CanModerateHeavy}, CanUnmoderate={CanUnmoderate}, CanArchive={CanArchive}, CanReport={CanReport}, CanViewModerationReports={CanViewModerationReports}",
            EventId,
            _canEdit,
            _canDelete,
            _canManageTeam,
            _canPublish,
            _canCancel,
            _canModerateLight,
            _canModerateHeavy,
            _canUnmoderate,
            _canArchive,
            _canReport,
            _canViewModerationReports);
    }

    /// <summary>
    /// Gets the event type display name.
    /// </summary>
    private string GetEventTypeName()
    {
        return _eventDetails?.EventTypeFullName ?? "Event";
    }

    /// <summary>
    /// Maps the event status master code to a MudBlazor Color for chips.
    /// </summary>
    private Color GetStatusChipColor() => _eventDetails?.EventStatusMasterCode switch
    {
        PublishedStatusMasterCode => Color.Success,
        DraftStatusMasterCode => Color.Default,
        CancelledStatusMasterCode => Color.Error,
        CompletedStatusMasterCode => Color.Info,
        ModeratedStatusMasterCode => Color.Error,
        "POSTPONED" => Color.Warning,
        _ => Color.Default
    };

    private bool IsCancelledEvent() =>
        _eventDetails?.EventStatusMasterCode == CancelledStatusMasterCode;

    private bool IsPastEvent() =>
        _eventDetails?.LastSessionDate != null &&
        _eventDetails.LastSessionDate < Clock.GetUtcNow();

    /// <summary>
    /// Gets the formatted date display string.
    /// </summary>
    private string GetDateDisplay()
    {
        // Use session data if available, otherwise fall back to event dates
        if (_primarySession != null && _primarySession.StartTime.HasValue && _primarySession.EndTime.HasValue)
        {
            var start = _primarySession.StartTime.Value.LocalDateTime;
            var end = _primarySession.EndTime.Value.LocalDateTime;

            if (start.Date == end.Date)
            {
                return $"{start:dddd d MMMM yyyy} • {start:HH:mm} - {end:HH:mm}";
            }

            return $"{start:dd/MM/yyyy HH:mm} - {end:dd/MM/yyyy HH:mm}";
        }

        if (_eventDetails == null) return string.Empty;

        if (_eventDetails.FirstSessionDate.HasValue && _eventDetails.LastSessionDate.HasValue)
        {
            var start = _eventDetails.FirstSessionDate.Value.LocalDateTime;
            var end = _eventDetails.LastSessionDate.Value.LocalDateTime;

            if (start.Date == end.Date)
            {
                return $"{start:dddd d MMMM yyyy}";
            }

            return $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
        }

        return "Date TBD";
    }

    /// <summary>
    /// Loads the server-evaluated disclosure for this event. The anonymous public projection is always
    /// read; the attendee projection is layered on top only for a signed-in visitor, and the server —
    /// not this page — decides whether that visitor's registration unlocks anything extra.
    /// </summary>
    private async Task LoadEventLocationDisclosureAsync()
    {
        _publicLocationView = null;
        _attendeeLocationView = null;

        IReadOnlyList<EventLocationPublicDto> publicLocations =
            await EventLocationService.GetPublicAsync(EventId);
        _publicLocationView = publicLocations
            .Select(EventLocationDisclosureView.FromPublic)
            .FirstOrDefault();

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        IReadOnlyList<EventLocationAttendeeDto> attendeeLocations =
            await EventLocationService.GetMyAccessAsync(EventId);
        _attendeeLocationView = attendeeLocations
            .Select(EventLocationDisclosureView.FromAttendee)
            .FirstOrDefault(view => _publicLocationView is null
                || view.EventLocationId == _publicLocationView.EventLocationId)
            ?? attendeeLocations.Select(EventLocationDisclosureView.FromAttendee).FirstOrDefault();
    }

    /// <summary>The disclosure this visitor should see, or null when nothing was released at all.</summary>
    private EventLocationDisclosureView? EffectiveLocationView =>
        _publicLocationView is null && _attendeeLocationView is null
            ? null
            : EventLocationDisclosureView.Prefer(_publicLocationView, _attendeeLocationView);

    /// <summary>
    /// Generates a color code based on event type for placeholder images.
    /// </summary>
    private string GetEventColor()
    {
        return EventColorHelper.GetColorByTypeName(_eventDetails?.EventTypeFullName);
    }

    private string GetHeroStyle()
    {
        return AppearanceStyleBuilder.BuildHeroStyle(_appearance, $"#{GetEventColor()}");
    }

    /// <summary>
    /// Checks if the event is organized by an organization (vs a user).
    /// </summary>
    private bool IsOrganizedByOrganization()
    {
        return _eventDetails?.ActorTypeFullName?.Equals("Organization", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Gets the organizer display name from the actor info.
    /// </summary>
    private string GetOrganizerName()
    {
        return _eventDetails?.ActorDisplayName ?? "Unknown Organizer";
    }

    private string GetModerationReportsHref()
    {
        var eventId = _eventDetails?.Id is { } id && id != Guid.Empty ? id : EventId;
        return $"/events/{eventId}/moderation/reports";
    }

    /// <summary>
    /// Gets the organizer profile picture URL.
    /// </summary>
    private string GetOrganizerProfilePicture()
    {
        return _eventDetails?.ActorProfilePictureUri ?? string.Empty;
    }

    /// <summary>
    /// Returns the profile page URL for the organizer actor, or null if no public profile exists for that actor type.
    /// Organization (ActorTypeId=2) → /organization/profile/{id}; Group (ActorTypeId=4) → /group/profile/{id}.
    /// </summary>
    private string? GetOrganizerProfileUrl()
    {
        if (_eventDetails?.ActorId == null || _eventDetails.ActorTypeId == null) return null;
        return _eventDetails.ActorTypeId.Value switch
        {
            2 => $"/organization/profile/{_eventDetails.ActorId.Value}",  // Organization
            4 => $"/group/profile/{_eventDetails.ActorId.Value}",          // Group
            _ => null
        };
    }

    private void NavigateToOrganizer()
    {
        var url = GetOrganizerProfileUrl();
        if (url != null)
            Navigation.NavigateTo(url);
    }

    private void HandleOrganizerKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            NavigateToOrganizer();
        }
    }

    #region OG Metadata Helpers

    private string GetCanonicalUrl()
    {
        var path = EventUrlHelper.BuildPublicPath(_eventDetails?.Slug, _eventDetails?.PublicCode) ?? "/events";
        return CanonicalUrlHelper.Build(Navigation, path);
    }

    private string GetMetaDescription()
    {
        if (string.IsNullOrWhiteSpace(_eventDetails?.Description))
            return $"{_eventDetails?.Title} — Event on {BrandDisplayName}";

        var plainText = _eventDetails.Description
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Trim();

        return plainText.Length > 200
            ? string.Concat(plainText.AsSpan(0, 197), "...")
            : plainText;
    }

    private string GetAboutText()
    {
        if (!string.IsNullOrWhiteSpace(_eventDetails?.Content))
            return _eventDetails.Content;

        return string.IsNullOrWhiteSpace(_eventDetails?.Description)
            ? "No event content available."
            : _eventDetails.Description;
    }

    private string GetOgImageUrl()
    {
        var slugCode = EventUrlHelper.BuildPublicSlugCode(_eventDetails?.Slug, _eventDetails?.PublicCode);
        return slugCode is null
            ? string.Empty
            : CanonicalUrlHelper.Build(Navigation, $"/api/event/public/{slugCode}/og-image");
    }

    private bool ShouldNoIndexEvent()
    {
        if (_eventDetails is null)
        {
            return true;
        }

        return !IsCrawlableStatus(_eventDetails.EventStatusMasterCode)
               || !string.Equals(_eventDetails.VisibilityTypeMasterCode, PublicVisibilityMasterCode, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRenderEventStructuredData() =>
        _eventDetails is not null
        && !ShouldNoIndexEvent()
        && !string.IsNullOrWhiteSpace(_eventDetails.Title);

    private static bool IsCrawlableStatus(string? status) =>
        string.Equals(status, PublishedStatusMasterCode, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, CompletedStatusMasterCode, StringComparison.OrdinalIgnoreCase);

    private string GetEventStructuredDataJson()
    {
        if (_eventDetails is null)
        {
            return "{}";
        }

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Event",
            ["name"] = _eventDetails.Title,
            ["description"] = GetMetaDescription(),
            ["url"] = GetCanonicalUrl(),
            ["eventStatus"] = GetSchemaEventStatus()
        };

        AddIfNotBlank(data, "image", GetOgImageUrl());
        AddIfNotBlank(data, "startDate", _primarySession?.StartTime?.ToString("O"));
        AddIfNotBlank(data, "endDate", _primarySession?.EndTime?.ToString("O"));

        var organizer = BuildSchemaOrganizer();
        if (organizer is not null)
        {
            data["organizer"] = organizer;
        }

        var location = BuildSchemaLocation();
        if (location is not null)
        {
            data["location"] = location;
        }

        return JsonSerializer.Serialize(data, EventStructuredDataJsonOptions);
    }

    /// <summary>
    /// Structured data is crawler-facing, so it is built only from the anonymous public projection and
    /// only from coarse fields. Street, postcode, and coordinates are never emitted here even when the
    /// signed-in visitor can see them on the page: the markup is cached, scraped, and syndicated.
    /// </summary>
    private Dictionary<string, object?>? BuildSchemaLocation()
    {
        if (_publicLocationView is not { } view
            || view.IsToBeAnnounced
            || view.IsUnavailable)
        {
            return null;
        }

        var address = new Dictionary<string, object?> { ["@type"] = "PostalAddress" };
        AddIfNotBlank(address, "addressLocality", view.City);
        AddIfNotBlank(address, "addressCountry", view.Country);

        var location = new Dictionary<string, object?> { ["@type"] = "Place" };
        AddIfNotBlank(location, "name", view.VenueName);

        if (address.Count > 1)
        {
            location["address"] = address;
        }

        return location.Count > 1 ? location : null;
    }

    private string GetSchemaEventStatus() =>
        string.Equals(_eventDetails?.EventStatusMasterCode, CancelledStatusMasterCode, StringComparison.OrdinalIgnoreCase)
            ? SchemaEventCancelled
            : SchemaEventScheduled;

    private Dictionary<string, object?>? BuildSchemaOrganizer()
    {
        var name = GetOrganizerName();
        if (string.IsNullOrWhiteSpace(name) || name == "Unknown Organizer")
        {
            return null;
        }

        var organizer = new Dictionary<string, object?>
        {
            ["@type"] = IsOrganizedByOrganization() ? "Organization" : "Person",
            ["name"] = name
        };

        var organizerProfileUrl = GetOrganizerProfileUrl();
        if (!string.IsNullOrWhiteSpace(organizerProfileUrl))
        {
            organizer["url"] = CanonicalUrlHelper.Build(Navigation, organizerProfileUrl);
        }

        return organizer;
    }

    private static void AddIfNotBlank(Dictionary<string, object?> data, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            data[key] = value;
        }
    }

    private static string TruncateTitle(string title)
    {
        if (title.Length <= 70)
            return title;

        var truncated = title[..67];
        var lastSpace = truncated.LastIndexOf(' ');
        return lastSpace > 0
            ? string.Concat(truncated.AsSpan(0, lastSpace), "...")
            : string.Concat(truncated, "...");
    }

    #endregion

    #region Share

    private async Task ShareEventAsync()
    {
        var url = GetCanonicalUrl();

        if (await BrowserActionInterop.ShareAsync(_eventDetails?.Title ?? "Event", url))
        {
            return;
        }

        if (await BrowserActionInterop.CopyTextAsync(url))
        {
            Snackbar.Add("Link copied to clipboard!", Severity.Success,
                options => options.VisibleStateDuration = 2000);
            return;
        }

        Logger.LogWarning("Failed to copy event link to clipboard");
        Snackbar.Add("Could not copy link", Severity.Error);
    }

    #endregion

    #region Calendar Integration

    private async Task AddToGoogleCalendarAsync()
    {
        if (_eventDetails is null || _primarySession is null)
            return;

        var start = _primarySession.StartTime!.Value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = _primarySession.EndTime!.Value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var title = Uri.EscapeDataString(_eventDetails.Title);
        var details = Uri.EscapeDataString(
            GetMetaDescription() + "\n\n" + GetCanonicalUrl());
        var url = $"https://calendar.google.com/calendar/r/eventedit?text={title}&dates={start}/{end}&details={details}";

        await JsRuntime.InvokeVoidAsync("open", url, "_blank");
    }

    private string GetCalendarDownloadUrl()
    {
        return EventId == Guid.Empty
            ? "#"
            : $"/api/event/{EventId}/calendar";
    }

    private async Task DownloadIcsFileAsync()
    {
        if (_eventDetails is null || _primarySession is null)
            return;

        var ics = GenerateIcsContent();
        var bytes = System.Text.Encoding.UTF8.GetBytes(ics);
        var base64 = Convert.ToBase64String(bytes);
        var fileName = SanitizeFileName(_eventDetails.Title) + ".ics";

        var downloaded = await BrowserActionInterop.DownloadBase64FileAsync(base64, fileName, "text/calendar");
        if (!downloaded)
        {
            Snackbar.Add("Could not download calendar file", Severity.Error);
        }
    }

    private string GenerateIcsContent()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine(IcsFoldLine($"PRODID:-//{IcsEscape(BrandDisplayName)}//Event Platform//EN"));
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:PUBLISH");
        sb.AppendLine("BEGIN:VEVENT");

        var now = Clock.GetUtcNow().UtcDateTime;
        sb.AppendLine($"DTSTAMP:{now:yyyyMMdd'T'HHmmss'Z'}");
        sb.AppendLine($"UID:{EventId}@{GetCalendarUidHost()}");

        if (_primarySession is not null)
        {
            sb.AppendLine($"DTSTART:{_primarySession.StartTime!.Value.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
            sb.AppendLine($"DTEND:{_primarySession.EndTime!.Value.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");

        }

        sb.AppendLine(IcsFoldLine($"SUMMARY:{IcsEscape(_eventDetails!.Title)}"));

        var description = GetMetaDescription() + "\\n\\n" + GetCanonicalUrl();
        sb.AppendLine(IcsFoldLine($"DESCRIPTION:{IcsEscape(description)}"));
        sb.AppendLine(IcsFoldLine($"URL:{GetCanonicalUrl()}"));
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        return sb.ToString();
    }

    private static string IcsEscape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    private string GetCalendarUidHost()
    {
        return Uri.TryCreate(Navigation.BaseUri, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "event-platform.local";
    }

    private static string NormalizeBrandDisplayName(string? brandDisplayName)
    {
        return string.IsNullOrWhiteSpace(brandDisplayName)
            ? DefaultBrandDisplayName
            : brandDisplayName.Trim();
    }

    /// <summary>
    /// Folds an ICS content line at 75 octets per RFC 5545 §3.1.
    /// Continuation lines begin with a single space character.
    /// </summary>
    private static string IcsFoldLine(string line)
    {
        const int maxOctets = 75;
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        if (bytes.Length <= maxOctets)
            return line;

        var sb = new System.Text.StringBuilder();
        int offset = 0;
        bool first = true;

        while (offset < bytes.Length)
        {
            int limit = first ? maxOctets : maxOctets - 1; // account for leading space on continuation
            int end = Math.Min(offset + limit, bytes.Length);

            // Avoid splitting in the middle of a multi-byte UTF-8 character
            while (end < bytes.Length && end > offset && (bytes[end] & 0xC0) == 0x80)
                end--;

            if (!first)
                sb.Append("\r\n ");

            sb.Append(System.Text.Encoding.UTF8.GetString(bytes, offset, end - offset));
            offset = end;
            first = false;
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return string.Concat(name.Where(c => !invalid.Contains(c))).Trim();
    }

    #endregion

    private async Task PublishEventAsync()
    {
        if (!_canPublish || _eventDetails is null || _isProcessingEventAction)
            return;

        var selectedSessionsToPublish = await ResolveSessionsToPublishOnEventPublishAsync();
        if (selectedSessionsToPublish is null)
            return;

        if (_eventDetails.ConcurrencyStamp is not Guid expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            Snackbar.Add("Refresh the event before publishing.", Severity.Warning);
            return;
        }

        _isProcessingEventAction = true;

        try
        {
            var response = await EventService.PublishEventAsync(EventId, expectedConcurrencyStamp);
            if (response?.Success == true)
            {
                var sessionPublishResult = await PublishSelectedSessionsAsync(selectedSessionsToPublish);
                if (sessionPublishResult.FailedCount > 0)
                {
                    Snackbar.Add(
                        $"Event published. {sessionPublishResult.PublishedCount} session{(sessionPublishResult.PublishedCount == 1 ? "" : "s")} published; {sessionPublishResult.FailedCount} failed.",
                        Severity.Warning);
                }
                else if (sessionPublishResult.PublishedCount > 0)
                {
                    Snackbar.Add(
                        $"Event and {sessionPublishResult.PublishedCount} session{(sessionPublishResult.PublishedCount == 1 ? "" : "s")} published.",
                        Severity.Success);
                }
                else
                {
                    Snackbar.Add("Event published.", Severity.Success);
                }

                await LoadEventDataAsync();
                return;
            }

            Snackbar.Add(response?.Message ?? "Event could not be published.", Severity.Error);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing event {EventId}", EventId);
            Snackbar.Add("Event could not be published.", Severity.Error);
        }
        finally
        {
            _isProcessingEventAction = false;
        }
    }

    private async Task<IReadOnlyList<EventSessionPublishSelectionDialog.EventSessionPublishSelection>?> ResolveSessionsToPublishOnEventPublishAsync()
    {
        if (CanRequestManagedSessions)
        {
            _eventSessions = RemovePhysicalLocationData(
                await EventSessionService.GetSessionsByEventAsync(EventId, includeManagedSessions: true));
        }

        var sessions = _eventSessions?
            .Where(session => session.Id is { } sessionId && sessionId != Guid.Empty)
            .OrderBy(session => session.StartTime ?? DateTimeOffset.MaxValue)
            .ThenBy(session => session.SortOrder)
            .ThenBy(session => session.Title)
            .ToArray() ?? [];

        if (sessions.Length <= 1)
        {
            var session = sessions.FirstOrDefault(session =>
                session.ConcurrencyStamp is { } concurrencyStamp
                && concurrencyStamp != Guid.Empty
                && session.HasHalLink(PublishLinkRelation));

            return session is null
                ? []
                : [new EventSessionPublishSelectionDialog.EventSessionPublishSelection(session.Id!.Value, session.ConcurrencyStamp!.Value)];
        }

        await AccessibilityFocusService.SaveFocusAsync();
        try
        {
            var parameters = new DialogParameters<EventSessionPublishSelectionDialog>
            {
                { dialog => dialog.Sessions, sessions }
            };

            var dialog = await DialogService.ShowAsync<EventSessionPublishSelectionDialog>(
                "Publish Sessions",
                parameters,
                DialogOptionsFactory.Confirmation());
            var result = await dialog.Result;

            return result is { Canceled: false, Data: EventSessionPublishSelectionDialog.EventSessionPublishSelectionResult selection }
                ? selection.Sessions
                : null;
        }
        finally
        {
            await AccessibilityFocusService.RestoreFocusAsync();
        }
    }

    private async Task<(int PublishedCount, int FailedCount)> PublishSelectedSessionsAsync(
        IReadOnlyList<EventSessionPublishSelectionDialog.EventSessionPublishSelection> selectedSessions)
    {
        var publishedCount = 0;
        var failedCount = 0;

        foreach (var session in selectedSessions)
        {
            var response = await EventSessionService.PublishEventSessionAsync(
                session.SessionId,
                session.ExpectedConcurrencyStamp);

            if (response?.Success == true)
            {
                publishedCount++;
                continue;
            }

            failedCount++;
            Logger.LogWarning(
                "Event session {SessionId} could not be published after event {EventId} published: {Message}",
                session.SessionId,
                EventId,
                response?.Message);
        }

        return (publishedCount, failedCount);
    }

    private Task CancelEventAsync() =>
        ConfirmAndExecuteLifecycleActionAsync(
            "Cancel Event",
            $"Cancel \"{_eventDetails?.Title}\"? Registrations and public calls to action will stop being available.",
            "Cancel Event",
            "Event cancelled.",
            () => EventService.CancelEventAsync(EventId, _eventDetails?.ConcurrencyStamp ?? Guid.Empty, default));

    private Task ArchiveEventAsync() =>
        ConfirmAndExecuteLifecycleActionAsync(
            "Archive Event",
            $"Archive \"{_eventDetails?.Title}\"? Archived events are removed from public event discovery.",
            "Archive Event",
            "Event archived.",
            () => EventService.ArchiveEventAsync(EventId, _eventDetails?.ConcurrencyStamp ?? Guid.Empty, default),
            navigateToEvents: true);

    private async Task ModerateEventAsync()
    {
        if (!_canModerateLight || _eventDetails is null || _isProcessingEventAction)
            return;

        var dialogResult = await ShowModerationReasonDialogAsync(
            title: "Moderate Event",
            message: $"Moderate \"{_eventDetails.Title}\"? This hides the event from public discovery.",
            confirmText: "Moderate",
            cancelText: "Keep Event",
            confirmIcon: Icons.Material.Filled.Gavel,
            titleIcon: Icons.Material.Filled.AdminPanelSettings,
            confirmColor: Color.Warning,
            alertSeverity: Severity.Warning,
            reasonOptions: LightModerationReasonOptions);

        if (dialogResult is null)
            return;

        _isProcessingEventAction = true;

        try
        {
            var response = await EventModerationService.ModerateEventLightAsync(EventId, reasonCode: dialogResult.ReasonCode);
            if (response?.Success != true)
            {
                Snackbar.Add(response?.Message ?? "Event could not be moderated.", Severity.Error);
                return;
            }

            Snackbar.Add("Event moderated.", Severity.Success);
            await LoadEventDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error moderating event {EventId}", EventId);
            Snackbar.Add("Event could not be moderated.", Severity.Error);
        }
        finally
        {
            _isProcessingEventAction = false;
        }
    }

    private async Task HeavyRedactEventAsync()
    {
        if (!_canModerateHeavy || _eventDetails is null || _isProcessingEventAction)
            return;

        var dialogResult = await ShowModerationReasonDialogAsync(
            title: "Heavy Redact Event",
            message: $"Permanently redact \"{_eventDetails.Title}\" and delete event images?",
            confirmText: "Redact Event",
            cancelText: "Keep Event",
            confirmIcon: Icons.Material.Filled.DeleteForever,
            titleIcon: Icons.Material.Filled.Report,
            confirmColor: Color.Error,
            alertSeverity: Severity.Error,
            reasonOptions: HeavyModerationReasonOptions,
            requiresIrreversibleConfirmation: true);

        if (dialogResult is null)
            return;

        _isProcessingEventAction = true;

        try
        {
            var response = await EventModerationService.ModerateEventHeavyAsync(EventId, reasonCode: dialogResult.ReasonCode);
            if (response?.Success != true)
            {
                Snackbar.Add(response?.Message ?? "Event could not be heavy moderated.", Severity.Error);
                return;
            }

            Snackbar.Add("Event redacted.", Severity.Success);
            await LoadEventDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error heavy moderating event {EventId}", EventId);
            Snackbar.Add("Event could not be heavy moderated.", Severity.Error);
        }
        finally
        {
            _isProcessingEventAction = false;
        }
    }

    private async Task UnmoderateEventAsync()
    {
        if (!_canUnmoderate || _eventDetails is null || _isProcessingEventAction)
            return;

        var dialogResult = await ShowModerationReasonDialogAsync(
            title: "Unmoderate Event",
            message: $"Restore \"{_eventDetails.Title}\" to published visibility?",
            confirmText: "Restore",
            cancelText: "Keep Moderated",
            confirmIcon: Icons.Material.Filled.Restore,
            titleIcon: Icons.Material.Filled.Verified,
            confirmColor: Color.Success,
            alertSeverity: Severity.Info,
            reasonOptions: UnmoderationReasonOptions);

        if (dialogResult is null)
            return;

        _isProcessingEventAction = true;

        try
        {
            var response = await EventModerationService.UnmoderateEventAsync(EventId, reasonCode: dialogResult.ReasonCode);
            if (response?.Success != true)
            {
                Snackbar.Add(response?.Message ?? "Event could not be unmoderated.", Severity.Error);
                return;
            }

            Snackbar.Add("Event restored.", Severity.Success);
            await LoadEventDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error unmoderating event {EventId}", EventId);
            Snackbar.Add("Event could not be unmoderated.", Severity.Error);
        }
        finally
        {
            _isProcessingEventAction = false;
        }
    }

    private async Task<EventModerationDialogResult?> ShowModerationReasonDialogAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        string confirmIcon,
        string titleIcon,
        Color confirmColor,
        Severity alertSeverity,
        IReadOnlyList<EventModerationReasonOption> reasonOptions,
        bool requiresIrreversibleConfirmation = false)
    {
        var parameters = new DialogParameters<EventModerationReasonDialog>
        {
            { dialog => dialog.DialogTitle, title },
            { dialog => dialog.Message, message },
            { dialog => dialog.ConfirmText, confirmText },
            { dialog => dialog.CancelText, cancelText },
            { dialog => dialog.ConfirmIcon, confirmIcon },
            { dialog => dialog.TitleIcon, titleIcon },
            { dialog => dialog.ConfirmColor, confirmColor },
            { dialog => dialog.AlertSeverity, alertSeverity },
            { dialog => dialog.ReasonOptions, reasonOptions },
            { dialog => dialog.RequiresIrreversibleConfirmation, requiresIrreversibleConfirmation }
        };

        await AccessibilityFocusService.SaveFocusAsync();
        try
        {
            var dialog = await EventModerationReasonDialog.ShowAsync(
                DialogService,
                title,
                parameters,
                DialogOptionsFactory.Confirmation());
            var result = await dialog.Result;

            return result is { Canceled: false, Data: EventModerationDialogResult dialogResult }
                ? dialogResult
                : null;
        }
        finally
        {
            await AccessibilityFocusService.RestoreFocusAsync();
        }
    }

    private async Task OpenReportEventDialogAsync()
    {
        if (!_canReport || _eventDetails is null)
        {
            return;
        }

        if (!await IsAuthenticatedForProtectedActionAsync())
        {
            await AccessibilityFocusService.SaveFocusAsync();
            await LoginPromptDialog.ShowAsync(
                DialogService,
                BuildReportReturnPath(),
                ReportLoginPromptMessage,
                ReportLoginPromptTitle,
                "Sign in",
                "Cancel");
            await AccessibilityFocusService.RestoreFocusAsync();
            return;
        }

        var parameters = new DialogParameters<ReportEventDialog>
        {
            { dialog => dialog.EventId, EventId },
            { dialog => dialog.EventTitle, _eventDetails.Title }
        };

        await AccessibilityFocusService.SaveFocusAsync();
        try
        {
            var dialog = await ReportEventDialog.ShowAsync(
                DialogService,
                "Report Event",
                parameters,
                DialogOptionsFactory.Medium());
            var result = await dialog.Result;
            if (result is { Canceled: false, Data: EventReportSubmissionResult { Success: true } reportResult })
            {
                Snackbar.Add("Event report submitted.", Severity.Success);
                if (reportResult.ReportId is not null)
                {
                    await AnnouncerService.AnnouncePoliteAsync("Event report submitted. You can track it from My Reports.");
                }
            }
        }
        finally
        {
            await AccessibilityFocusService.RestoreFocusAsync();
        }
    }

    private async Task<bool> IsAuthenticatedForProtectedActionAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        _isAuthenticated = authState.User.Identity?.IsAuthenticated == true;
        return _isAuthenticated;
    }

    private async Task TryOpenPendingReportDialogAsync()
    {
        if (_hasHandledReportIntent || !HasReportIntent() || !_canReport || _eventDetails is null)
        {
            return;
        }

        _hasHandledReportIntent = true;
        RemoveReportIntentFromUrl();
        await OpenReportEventDialogAsync();
    }

    private bool HasReportIntent()
        => string.Equals(ReportIntent, ReportIntentQueryValue, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(ReportIntent, "true", StringComparison.OrdinalIgnoreCase);

    private string BuildReportReturnPath()
    {
        var reportUri = Navigation.GetUriWithQueryParameter(ReportIntentQueryParameter, ReportIntentQueryValue);
        return Navigation.ToAbsoluteUri(reportUri).PathAndQuery;
    }

    private void RemoveReportIntentFromUrl()
    {
        var cleanUri = Navigation.GetUriWithQueryParameter(ReportIntentQueryParameter, (string?)null);
        Navigation.NavigateTo(cleanUri, replace: true);
    }

    private async Task ConfirmAndExecuteLifecycleActionAsync(
        string title,
        string message,
        string yesText,
        string successMessage,
        Func<Task<BaseCommandResponseOfGuid?>> action,
        bool navigateToEvents = false)
    {
        if (_eventDetails is null || _isProcessingEventAction)
            return;

        await AccessibilityFocusService.SaveFocusAsync();
        bool? confirmed;
        try
        {
            confirmed = await DialogService.ShowMessageBoxAsync(
                title,
                message,
                yesText: yesText,
                cancelText: "Keep Event");
        }
        finally
        {
            await AccessibilityFocusService.RestoreFocusAsync();
        }

        if (confirmed != true)
            return;

        _isProcessingEventAction = true;

        try
        {
            var response = await action();
            if (response?.Success != true)
            {
                Snackbar.Add(response?.Message ?? "Event action could not be completed.", Severity.Error);
                return;
            }

            Snackbar.Add(successMessage, Severity.Success);
            if (navigateToEvents)
            {
                Navigation.NavigateTo("/events");
                return;
            }

            await LoadEventDataAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing lifecycle action on event {EventId}", EventId);
            Snackbar.Add("Event action could not be completed.", Severity.Error);
        }
        finally
        {
            _isProcessingEventAction = false;
        }
    }

    private string GetEditActionLabel() =>
        string.Equals(_eventDetails?.EventStatusMasterCode, DraftStatusMasterCode, StringComparison.OrdinalIgnoreCase)
            ? "Return to Edit"
            : "Edit";

    /// <summary>
    /// Opens the delete confirmation dialog.
    /// </summary>
    private async Task OpenDeleteDialog()
    {
        if (_eventDetails == null) return;

        var parameters = new DialogParameters
        {
            { "EventId", EventId },
            { "EventTitle", _eventDetails.Title }
        };

        var options = DialogOptionsFactory.Small();

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await DeleteEventDialog.ShowAsync(
            DialogService,
            "Delete Event",
            parameters,
            options);
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result != null && !result.Canceled)
        {
            // Dialog already handled deletion and snackbar notification.
            // Return to the public event catalog because the legacy My Events page was removed.
            Navigation.NavigateTo("/events");
        }
    }

    #region Event Aspects Dialog Methods

    /// <summary>
    /// Opens the dialog to add a new Islamic aspect to the event.
    /// </summary>
    private async Task OpenAddIslamicAspectDialog()
    {
        await OpenIslamicAspectDialogAsync(existingAspect: null);
    }

    /// <summary>
    /// Opens the dialog to edit the existing Islamic aspect.
    /// </summary>
    private async Task OpenEditIslamicAspectDialog()
    {
        await OpenIslamicAspectDialogAsync(_islamicAspect);
    }

    /// <summary>
    /// Opens the Islamic aspect dialog for add or edit operations.
    /// </summary>
    private async Task OpenIslamicAspectDialogAsync(EventIslamicAspectDto? existingAspect)
    {
        var parameters = new DialogParameters
        {
            { "EventId", EventId },
            { "ExistingAspect", existingAspect }
        };

        var options = DialogOptionsFactory.Medium();

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await IslamicAspectEditDialog.ShowAsync(
            DialogService,
            existingAspect == null ? "Add Islamic Characteristics" : "Edit Islamic Characteristics",
            parameters,
            options);

        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result != null && !result.Canceled)
        {
            // Reload the aspect to reflect changes
            await ReloadIslamicAspectAsync();
        }
    }

    /// <summary>
    /// Opens the dialog to add a new Tech aspect to the event.
    /// </summary>
    private async Task OpenAddTechAspectDialog()
    {
        await OpenTechAspectDialogAsync(existingAspect: null);
    }

    /// <summary>
    /// Opens the dialog to edit the existing Tech aspect.
    /// </summary>
    private async Task OpenEditTechAspectDialog()
    {
        await OpenTechAspectDialogAsync(_techAspect);
    }

    /// <summary>
    /// Opens the Tech aspect dialog for add or edit operations.
    /// </summary>
    private async Task OpenTechAspectDialogAsync(EventTechAspectDto? existingAspect)
    {
        var parameters = new DialogParameters
        {
            { "EventId", EventId },
            { "ExistingAspect", existingAspect }
        };

        var options = DialogOptionsFactory.Medium();

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await TechAspectEditDialog.ShowAsync(
            DialogService,
            existingAspect == null ? "Add Tech Characteristics" : "Edit Tech Characteristics",
            parameters,
            options);

        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result != null && !result.Canceled)
        {
            // Reload the aspect to reflect changes
            await ReloadTechAspectAsync();
        }
    }

    /// <summary>
    /// Shows confirmation dialog and deletes the Islamic aspect if confirmed.
    /// </summary>
    private async Task ConfirmDeleteIslamicAspect()
    {
        await AccessibilityFocusService.SaveFocusAsync();
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete Islamic Characteristics",
            "Are you sure you want to remove the Islamic characteristics from this event? This action cannot be undone.",
            yesText: "Delete",
            cancelText: "Cancel");
        await AccessibilityFocusService.RestoreFocusAsync();

        if (confirmed == true)
        {
            try
            {
                var success = await EventAspectService.DeleteIslamicAspectAsync(EventId);
                if (success)
                {
                    _islamicAspect = null;
                }
                else
                {
                    _errorMessage = "Failed to remove Islamic characteristics";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error deleting Islamic aspect for event {EventId}", EventId);
                _errorMessage = "An error occurred while removing Islamic characteristics";
            }
        }
    }

    /// <summary>
    /// Shows confirmation dialog and deletes the Tech aspect if confirmed.
    /// </summary>
    private async Task ConfirmDeleteTechAspect()
    {
        await AccessibilityFocusService.SaveFocusAsync();
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete Tech Characteristics",
            "Are you sure you want to remove the Tech characteristics from this event? This action cannot be undone.",
            yesText: "Delete",
            cancelText: "Cancel");
        await AccessibilityFocusService.RestoreFocusAsync();

        if (confirmed == true)
        {
            try
            {
                var success = await EventAspectService.DeleteTechAspectAsync(EventId);
                if (success)
                {
                    _techAspect = null;
                }
                else
                {
                    _errorMessage = "Failed to remove Tech characteristics";
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error deleting Tech aspect for event {EventId}", EventId);
                _errorMessage = "An error occurred while removing Tech characteristics";
            }
        }
    }

    /// <summary>
    /// Reloads only the Islamic aspect after add/edit operations.
    /// </summary>
    private async Task ReloadIslamicAspectAsync()
    {
        try
        {
            _islamicAspect = await EventAspectService.GetIslamicAspectAsync(EventId, includeManaged: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reloading Islamic aspect for event {EventId}", EventId);
        }
    }

    /// <summary>
    /// Reloads only the Tech aspect after add/edit operations.
    /// </summary>
    private async Task ReloadTechAspectAsync()
    {
        try
        {
            _techAspect = await EventAspectService.GetTechAspectAsync(EventId, includeManaged: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reloading Tech aspect for event {EventId}", EventId);
        }
    }

    #endregion

    // ── Tag/Category management ──

    private void OpenDetailTagManagement()
    {
        _detailTagCatMode = TagCategoryMode.Tags;
        _detailTagCatInitialIds = (_eventDetails?.Tags?
            .Where(t => t.Id.HasValue)
            .Select(t => t.Id!.Value)
            .ToList() ?? new List<Guid>()).AsReadOnly();
        _showDetailTagCatPopup = true;
    }

    private void OpenDetailCategoryManagement()
    {
        _detailTagCatMode = TagCategoryMode.Categories;
        _detailTagCatInitialIds = (_eventDetails?.Categories?
            .Where(c => c.Id.HasValue)
            .Select(c => c.Id!.Value)
            .ToList() ?? new List<Guid>()).AsReadOnly();
        _showDetailTagCatPopup = true;
    }

    private async Task HandleDetailTagCatSaved(IReadOnlyCollection<Guid> newIds)
    {
        var label = _detailTagCatMode == TagCategoryMode.Tags ? "Tag" : "Category";
        Snackbar.Add($"{label} changes saved.", Severity.Success);

        try
        {
            var detail = await EventService.GetEventByIdAsync(EventId);
            if (detail != null)
            {
                _eventDetails = detail;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing event after {Label} changes", label);
        }
    }

    // ── UI Formatting Helpers ──

    private string GetWrapperStyle()
    {
        return BuildEventBackgroundSurfaceStyle();
    }

    private bool HasActualCoverImage => !_imageLoadFailed && !string.IsNullOrEmpty(GetImageUrl());

    private string? GetImageUrl()
    {
        if (!string.IsNullOrEmpty(_eventDetails?.FeaturedImageUri))
        {
            return _eventDetails.FeaturedImageUri;
        }

        var publicFeaturedImageUrl = GetFeaturedImagePublicUrl();
        if (!string.IsNullOrWhiteSpace(publicFeaturedImageUrl))
        {
            return publicFeaturedImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(_appearance.ImageUri))
        {
            return _appearance.ImageUri;
        }
        return null;
    }

    private string? GetFeaturedImagePublicUrl()
    {
        if (_eventDetails?.FeaturedImageId is not Guid imageId || imageId == Guid.Empty)
        {
            return null;
        }

        var baseUri = Navigation.BaseUri.TrimEnd('/');
        return $"{baseUri}/api/storageobject/{imageId}/content";
    }

    private string GetFallbackSvgDataUri()
    {
        return ImageHelper.GetEventImageUrl(null, _eventDetails?.Title ?? "Event", GetEventColor(), width: 300, height: 400);
    }

    private void PublishMainContentAppearance()
    {
        MainContentAppearanceState.Set(MainContentAppearanceOwner, BuildMainContentStyle());
    }

    private string BuildMainContentStyle()
    {
        if (string.IsNullOrWhiteSpace(_appearance.BackgroundColor))
        {
            return EventDetailMainContentStyle;
        }

        return BuildEventBackgroundSurfaceStyle(EventDetailMainContentStyle);
    }

    private string BuildEventBackgroundSurfaceStyle()
        => BuildEventBackgroundSurfaceStyle(additionalCss: null);

    private string BuildEventBackgroundSurfaceStyle(string? additionalCss)
    {
        if (string.IsNullOrWhiteSpace(_appearance.BackgroundColor))
        {
            return string.Empty;
        }

        var backgroundColor = _appearance.BackgroundColor.Trim();
        var settings = new AppearanceSettings
        {
            BackgroundColor = backgroundColor,
            BackgroundEffect = "None",
            ImageUri = string.Empty
        };

        return AppearanceStyleBuilder.BuildSurfaceStyle(settings, backgroundColor, additionalCss);
    }

    private void HandleImageError()
    {
        _imageLoadFailed = true;
    }

    public void Dispose()
    {
        MainContentAppearanceState.Clear(MainContentAppearanceOwner);
    }

    private string GetDateMonth() => _eventDetails?.FirstSessionDate?.ToString("MMM") ?? "TBD";
    private string GetDateDay() => _eventDetails?.FirstSessionDate?.ToString("dd") ?? "--";
    private string GetDateFull() => _eventDetails?.FirstSessionDate?.ToString("dddd, MMMM d, yyyy") ?? "Date to be announced";

    private string GetTimeRange()
    {
        if (_eventDetails == null) return string.Empty;

        var start = _eventDetails.FirstSessionDate?.ToString("d") ?? "Schedule pending";
        if (_eventDetails.LastSessionDate.HasValue && _eventDetails.LastSessionDate.Value != _eventDetails.FirstSessionDate)
        {
            var end = _eventDetails.LastSessionDate.Value.ToString("d");
            return $"{start} - {end}";
        }
        return start;
    }

    private bool IsDigitalEvent() =>
        string.Equals(_eventDetails?.EventFormatMasterCode, "DIGITAL", StringComparison.OrdinalIgnoreCase);

    private bool IsHybridEvent() =>
        string.Equals(_eventDetails?.EventFormatMasterCode, "HYBRID", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Format context that sits beside the disclosure card. It describes how the event runs and never
    /// makes a claim about whether a venue exists or will be revealed.
    /// </summary>
    private string? GetEventFormatHint()
    {
        if (IsDigitalEvent())
        {
            return "Online event";
        }

        return IsHybridEvent() ? "Hybrid event" : null;
    }

    private static ICollection<EventSessionListDto> RemovePhysicalLocationData(
        ICollection<EventSessionListDto>? sessions)
    {
        sessions ??= [];
        foreach (var session in sessions)
        {
            session.LocationId = null;
            session.LocationFullName = null;
            session.LocationCity = null;
            session.RoomId = null;
            session.RoomName = null;
            RemovePhysicalLocationExtensionData(session.AdditionalProperties);
        }

        return sessions;
    }

    private static ICollection<EventSessionAgendaItemListDto> RemovePhysicalLocationData(
        ICollection<EventSessionAgendaItemListDto>? items)
    {
        items ??= [];
        foreach (var item in items)
        {
            item.LocationFullName = null;
            RemovePhysicalLocationExtensionData(item.AdditionalProperties);
        }

        return items;
    }

    private static void RemovePhysicalLocationExtensionData(IDictionary<string, object>? extensionData)
    {
        if (extensionData is null || extensionData.Count == 0)
        {
            return;
        }

        foreach (var key in extensionData.Keys.Where(PhysicalLocationExtensionFields.Contains).ToArray())
        {
            extensionData.Remove(key);
        }
    }

    private string GetAudienceDisplay()
    {
        var gender = string.IsNullOrWhiteSpace(_eventDetails?.AudienceGenderFullName)
            ? "All genders"
            : _eventDetails!.AudienceGenderFullName;
        var age = string.IsNullOrWhiteSpace(_eventDetails?.AudienceAgeFullName)
            ? "All ages"
            : _eventDetails!.AudienceAgeFullName;
        return $"{gender} · {age}";
    }

    private string? GetTicketPriceSummaryDisplay() => TicketPriceSummaryFormatter.Format(_eventDetails);

    private string GetRegistrationPolicyDisplay()
    {
        if (!string.IsNullOrWhiteSpace(_eventDetails?.RegistrationPolicyFullName))
        {
            return _eventDetails.RegistrationPolicyFullName;
        }

        return _eventDetails?.ParticipationConfiguration?.AdvanceRegistrationObligationName
            ?? _eventDetails?.ParticipationConfiguration?.ParticipationHandlingModeName
            ?? "Participation details not specified";
    }

    private bool CanStartRegistration =>
        _eventDetails?.HasHalLink("start-registration") == true ||
        _eventDetails?.HasHalLink("sign-in-to-register") == true;

    private string? ExternalParticipationHref => _eventDetails?.GetHalHref("external-registration");

    private string ExternalParticipationLabel =>
        _eventDetails?.GetHalTitle("external-registration") ?? "Continue on external site";

    private bool HasParticipationAction => CanStartRegistration || ExternalParticipationHref is not null;

    private string T(string key, string fallback) => Translation.T(key, fallback);

    private string GetProgramSummary()
    {
        var count = _eventDetails?.SessionCount ?? _eventSessions?.Count ?? 0;
        return count switch
        {
            0 => "Program not published yet",
            1 => "1 program item",
            _ => $"{count} program items"
        };
    }
}
