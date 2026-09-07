using System.Globalization;
using System.Linq;
using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Forms;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.CustomProperties;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models.CustomProperties;
using Explore.Blazor.Client.Models.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Pages.Events.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events;

public partial class EventEdit : IDisposable
{
    private const string MainContentAppearanceOwner = nameof(EventEdit);

    [Inject] protected IEventService EventService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.IEventSessionService EventSessionService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService EventLookupService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.IDemographicLookupService DemographicLookupService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.ICultureLookupService CultureLookupService { get; set; } = null!;
    [Inject] protected IImageStorageService ImageStorageService { get; set; } = null!;
    [Inject] protected ICategoryService CategoryService { get; set; } = null!;
    [Inject] protected ITagService TagService { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected RouterStateService RouterState { get; set; } = null!;
    [Inject] protected IDialogService DialogService { get; set; } = null!;
    [Inject] protected ILogger<EventEdit> Logger { get; set; } = null!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AccessibilityAnnouncerService { get; set; } = default!;
    [Inject] private IEventRegistrationPolicyService RegistrationPolicyService { get; set; } = default!;
    [Inject] private ICustomPropertyDefinitionService CustomPropertyDefinitionService { get; set; } = default!;
    [Inject] private MainContentAppearanceState MainContentAppearanceState { get; set; } = default!;

    private Guid EventId { get; set; }

    private bool CanAddSession => currentEvent?.HasHalLink("add-session") == true;

    private bool CanManageProgramSections => currentEvent?.HasHalLink("add-session-group") == true;

    private bool CanRequestManagedSessions =>
        currentEvent?.HasHalLink("edit") == true ||
        CanAddSession ||
        CanManageProgramSections ||
        currentEvent?.HasHalLink("team") == true;

    private string ProgramSummary
    {
        get
        {
            if (_programSummary is not null)
            {
                var itemCount = GetProgramItems(_programSummary).Count();
                var sectionCount = _programSummary.Sections?.Count ?? 0;

                return itemCount switch
                {
                    0 => "No program items yet; start by adding a session or section/track.",
                    1 => sectionCount == 0
                        ? "1 program item saved; add a section or track when you are ready to organize it."
                        : $"1 program item saved across {sectionCount} program section{(sectionCount == 1 ? string.Empty : "s")}.",
                    _ => sectionCount == 0
                        ? $"{itemCount} program items saved; add sections or tracks when you are ready to organize them."
                        : $"{itemCount} program items saved across {sectionCount} program section{(sectionCount == 1 ? string.Empty : "s")}."
                };
            }

            return sessions.Count switch
            {
                0 => "No program items yet; start by adding a session or section/track.",
                1 => "1 program item saved; sections can organize it later.",
                _ => $"{sessions.Count} program items saved; sections can organize them later."
            };
        }
    }

    private string ProgramItemsSummary
    {
        get
        {
            if (_programSummary is not null)
            {
                var itemCount = GetProgramItems(_programSummary).Count();
                return itemCount switch
                {
                    0 => "No program items yet",
                    1 => "1 session saved",
                    _ => $"{itemCount} sessions saved"
                };
            }

            return sessions.Count switch
            {
                0 => "No program items yet",
                1 => "1 session saved",
                _ => $"{sessions.Count} sessions saved"
            };
        }
    }

    private string ProgramItemsDescription
    {
        get
        {
            if (_programSummary is not null)
            {
                var firstItem = GetProgramItems(_programSummary).FirstOrDefault();
                return firstItem is null
                    ? "Open the dedicated session composer for talks, workshops, panels, classes, and activities."
                    : BuildProgramItemMetadata(firstItem);
            }

            return sessions.Count == 0
                ? "Open the dedicated session composer for talks, workshops, panels, classes, and activities."
                : BuildProgramItemMetadata(sessions[0]);
        }
    }

    private string ProgramSectionsSummary
    {
        get
        {
            if (_programSummary is not null)
            {
                var assignedSections = _programSummary.Sections?
                    .Where(section => !string.Equals(section.SectionKey, "unassigned", StringComparison.OrdinalIgnoreCase))
                    .Count() ?? 0;

                if (_programSections.Count > assignedSections)
                    assignedSections = _programSections.Count;

                return assignedSections switch
                {
                    0 => "No sections or tracks yet",
                    1 => "1 section or track",
                    _ => $"{assignedSections} sections or tracks"
                };
            }

            return "No sections or tracks yet";
        }
    }

    private string BuildProgramItemMetadata(SessionEditorModel session)
    {
        var details = new List<string>
        {
            FormatSessionTimeRange(session),
            GetLocationName(session.LocationId) ?? "Location not set",
            FormatCapacity(session.MaxAudienceAttendees),
            GetRegistrationModeName(session.RegistrationModeId) ?? "Registration mode not set"
        };

        return string.Join(" · ", details);
    }

    private static string BuildProgramItemMetadata(EventProgramItemDto item)
    {
        var details = new List<string>
        {
            FormatProgramItemTimeRange(item),
            item.RoomName ?? item.LocationName ?? "Location not set",
            FormatCapacity(item.Capacity),
            item.RegistrationModeName ?? "Registration mode not set"
        };

        return string.Join(" · ", details);
    }

    private static IEnumerable<EventProgramItemDto> GetProgramItems(EventProgramSummaryDto summary)
    {
        return summary.Sections?
            .SelectMany(section => section.SessionGroups ?? [])
            .SelectMany(group => group.Days ?? [])
            .SelectMany(day => day.Items ?? [])
            ?? [];
    }

    private static string FormatProgramItemTimeRange(EventProgramItemDto item)
    {
        var localStart = item.LocalStartTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "--:--";
        var localEnd = item.LocalEndTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "--:--";
        var localDate = item.LocalDate?.ToString("ddd d MMM", CultureInfo.InvariantCulture) ?? "Date not set";

        return $"{localDate}, {localStart}–{localEnd}";
    }

    private static string FormatSessionTimeRange(SessionEditorModel session)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{session.StartTime:ddd d MMM, HH:mm}–{session.EndTime:HH:mm}");
    }

    private string? GetLocationName(Guid? locationId)
    {
        return locationId is null
            ? null
            : locations?.FirstOrDefault(location => location.Id == locationId)?.FullName;
    }

    private string? GetRegistrationModeName(int? registrationModeId)
    {
        return registrationModeId is null
            ? null
            : registrationModes?.FirstOrDefault(mode => mode.Id == registrationModeId)?.FullName;
    }

    private string BuildAudienceSummary()
    {
        var gender = GetLookupName(audienceGenders, updateDto?.AudienceGenderId) ?? "Any gender";
        var age = GetLookupName(audienceAges, updateDto?.AudienceAgeId) ?? "Any age";
        return $"{gender} · {age}";
    }

    private string BuildRegistrationSummary()
    {
        var policy = GetLookupName(registrationPolicies, updateDto?.RegistrationPolicyId) ?? "Default open registration";
        return $"{policy} · Capacity set per session";
    }

    private static string? GetLookupName(IEnumerable<VisibilityTypeListDto>? items, int? selectedId) =>
        items?.FirstOrDefault(item => item.Id == selectedId)?.FullName;

    private static string? GetLookupName(IEnumerable<AudienceGenderListDto>? items, int? selectedId) =>
        items?.FirstOrDefault(item => item.Id == selectedId)?.FullName;

    private static string? GetLookupName(IEnumerable<AudienceAgeListDto>? items, int? selectedId) =>
        items?.FirstOrDefault(item => item.Id == selectedId)?.FullName;

    private static string? GetLookupName(IEnumerable<EventRegistrationPolicyListDto>? items, int? selectedId) =>
        items?.FirstOrDefault(item => item.Id == selectedId)?.FullName;

    private static string? GetLookupName(IEnumerable<EventTypeListDto>? items, int? selectedId) =>
        items?.FirstOrDefault(item => item.Id == selectedId)?.FullName;

    private static string? GetLookupName(IEnumerable<MadhabListDto>? items, int? selectedId) =>
        items?.FirstOrDefault(item => item.Id == selectedId)?.FullName;

    private static List<string> GetSelectedNames(IEnumerable<CategoryListDto>? items, IReadOnlyCollection<Guid> selectedIds) =>
        items?
            .Where(item => item.Id.HasValue && selectedIds.Contains(item.Id.Value))
            .Select(item => item.FullName)
            .ToList() ?? [];

    private static List<string> GetSelectedNames(IEnumerable<TagListDto>? items, IReadOnlyCollection<Guid> selectedIds) =>
        items?
            .Where(item => item.Id.HasValue && selectedIds.Contains(item.Id.Value))
            .Select(item => item.FullName)
            .ToList() ?? [];

    private static string FormatCapacity(int? capacity)
    {
        return capacity.HasValue
            ? $"{capacity.Value} seats"
            : "Capacity not set";
    }

    // Event data
    private EventDto? currentEvent;
    private EventDraftEditModel? updateDto;

    // Form state
    private ICollection<EventTypeListDto>? eventTypes;
    private ICollection<EventStatusListDto>? eventStatuses;
    private ICollection<AudienceGenderListDto>? audienceGenders;
    private ICollection<AudienceAgeListDto>? audienceAges;
    private ICollection<EventFormatListDto>? eventFormats;
    private ICollection<VisibilityTypeListDto>? visibilityTypes;
    private ICollection<MadhabListDto>? madhabs;
    private ICollection<CategoryListDto>? allCategories;
    private ICollection<TagListDto>? allTags;
    private ICollection<EventSessionCreateLocationOptionDto>? locations;
    private ICollection<RegistrationModeListDto>? registrationModes;
    private ICollection<LanguageListDto>? languages;
    private ICollection<EventRegistrationPolicyListDto>? registrationPolicies;
    private bool isLoading = true;

    private EditContext _editContext = default!;
    private FormSubmitState _submitState = new();
    private ServerValidationErrorStore _errorStore = new();

    // Image upload state
    private string? imagePreviewUrl;
    private bool _isUploadingImage = false;
    private Guid? _uploadedImageStorageObjectId = null;
    private string? _uploadError;

    // Categories and Tags selection
    private IReadOnlyCollection<Guid> selectedCategoryIds = new HashSet<Guid>();
    private IReadOnlyCollection<Guid> selectedTagIds = new HashSet<Guid>();

    // Tag/Category management popup state
    private bool _showEditTagCatPopup;
    private TagCategoryMode _editTagCatMode;
    private IReadOnlyCollection<Guid> _editTagCatInitialIds = Array.Empty<Guid>();

    // Sessions
    private List<SessionEditorModel> sessions = new();
    private EventProgramSummaryDto? _programSummary;
    private List<HalResourceOfEventSessionGroupListDto> _programSections = new();
    private AppearanceSettings _appearance = new();

    private IReadOnlyList<CustomPropertyDefinitionDto> _eventCustomPropertyDefinitions = Array.Empty<CustomPropertyDefinitionDto>();
    private Dictionary<Guid, IReadOnlyList<CustomPropertyDefinitionDto>> _sessionCustomPropertyDefinitions = new();
    private Dictionary<Guid, string> _sessionCustomPropertyDefinitionLoadErrors = new();
    private string? _customPropertyDefinitionLoadError;

    // UI toggles
    private bool _showTimezoneSelector = false;
    private bool _programUpdatedOnReturn;
    private bool _isThemeStudioOpen;
    private string EventSettingsSummary => "Visibility, audience, registration, classification, categories, and tags.";
    private string VisibilitySummary => GetLookupName(visibilityTypes, updateDto?.VisibilityTypeId) ?? "Select visibility";
    private string AudienceSummary => BuildAudienceSummary();
    private string RegistrationSummary => BuildRegistrationSummary();
    private string EventTypeSummary => GetLookupName(eventTypes, updateDto?.EventTypeId) ?? "Select event type";
    private string MadhabSummary => GetLookupName(madhabs, updateDto?.MadhabId) ?? "No madhab set";
    private List<string> SelectedCategoryNames => GetSelectedNames(allCategories, selectedCategoryIds);
    private List<string> SelectedTagNames => GetSelectedNames(allTags, selectedTagIds);
    private string CategoriesSummary => SelectedCategoryNames.Count == 0 ? "No categories selected" : string.Join(", ", SelectedCategoryNames);
    private string TagsSummary => SelectedTagNames.Count == 0 ? "No tags selected" : string.Join(", ", SelectedTagNames);

    // Timezone
    private TimeZoneInfo _selectedTimezone = TimeZoneInfo.Utc;
    private string _selectedTimezoneDisplay => FormatTimezoneShort(_selectedTimezone);
    private static readonly IReadOnlyList<TimeZoneInfo> _allTimezones = TimeZoneInfo.GetSystemTimeZones();
    private bool isProcessing = false;

    protected override async Task OnInitializedAsync()
    {
        MainContentAppearanceState?.Set(MainContentAppearanceOwner, string.Empty);

        var eventIdStr = RouterState.GetParam("eventId");
        if (Guid.TryParse(eventIdStr, out var id))
        {
            EventId = id;
        }

        _programUpdatedOnReturn = HasProgramUpdatedReturnMarker();

        await LoadEventData();

        if (_programUpdatedOnReturn && !_submitState.HasError)
        {
            await AccessibilityAnnouncerService.AnnouncePoliteAsync("Program summary refreshed after saving the program item.");
        }
    }

    private bool HasProgramUpdatedReturnMarker()
    {
        var query = Navigation.ToAbsoluteUri(Navigation.Uri).Query;
        return query.Contains("programUpdated=1", StringComparison.OrdinalIgnoreCase)
            || query.Contains("programUpdated=true", StringComparison.OrdinalIgnoreCase);
    }

    // ========== Data Loading ==========

    private async Task LoadEventData()
    {
        try
        {
            isLoading = true;

            var eventTypesTask = EventLookupService.GetEventTypesAsync();
            var audienceGendersTask = DemographicLookupService.GetAudienceGendersAsync();
            var audienceAgesTask = DemographicLookupService.GetAudienceAgesAsync();
            var eventStatusesTask = EventLookupService.GetEventStatusesAsync();
            var eventFormatsTask = EventLookupService.GetEventFormatsAsync();
            var visibilityTypesTask = EventLookupService.GetVisibilityTypesAsync();
            var madhabsTask = CultureLookupService.GetMadhabsAsync();
            var categoriesTask = CategoryService.GetAllCategoriesAsync();
            var tagsTask = TagService.GetAllTagsAsync();
            var sessionCreateContextTask = EventSessionService.GetEventSessionCreateContextAsync(EventId);
            var registrationModesTask = EventLookupService.GetRegistrationModesAsync();
            var languagesTask = CultureLookupService.GetLanguagesAsync();
            var registrationPoliciesTask = RegistrationPolicyService.GetEventRegistrationPoliciesAsync();

            await Task.WhenAll(
                eventTypesTask, audienceGendersTask, audienceAgesTask, eventStatusesTask,
                eventFormatsTask, visibilityTypesTask,
                madhabsTask, categoriesTask, tagsTask, sessionCreateContextTask,
                registrationModesTask, languagesTask, registrationPoliciesTask);

            eventTypes = await eventTypesTask;
            eventStatuses = await eventStatusesTask;
            audienceGenders = await audienceGendersTask;
            audienceAges = await audienceAgesTask;
            eventFormats = await eventFormatsTask;
            visibilityTypes = await visibilityTypesTask;
            madhabs = await madhabsTask;
            allCategories = await categoriesTask;
            allTags = await tagsTask;
            locations = (await sessionCreateContextTask)?.Locations;
            registrationModes = await registrationModesTask;
            languages = await languagesTask;
            registrationPolicies = await registrationPoliciesTask;

            currentEvent = await EventService.GetEventByIdAsync(EventId);

            if (currentEvent != null)
            {
                PopulateFormFromEvent();

                var programSummaryTask = EventService.GetManagedEventProgramSummaryAsync(EventId);
                var sessionGroupsTask = EventSessionService.GetManagedSessionGroupsByEventAsync(EventId);
                var eventSessions = await EventSessionService.GetSessionsByEventAsync(
                    EventId,
                    includeManagedSessions: CanRequestManagedSessions);
                sessions = eventSessions?.Select(s => SessionEditorModel.FromDto(s)).ToList()
                           ?? new List<SessionEditorModel>();
                _programSections = (await sessionGroupsTask).ToList();
                _programSummary = await programSummaryTask;

                await LoadCustomPropertyDefinitionsAsync();

            }
            else
            {
                _submitState.Fail("Event not found");
                PublishMainContentAppearance();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading event data for editing");
            _submitState.Fail("Event details could not be loaded. Please refresh and try again.");
            PublishMainContentAppearance();
        }
        finally
        {
            isLoading = false;
        }
    }

    private void PopulateFormFromEvent()
    {
        if (currentEvent == null) return;

        updateDto = new EventDraftEditModel
        {
            ExpectedConcurrencyStamp = currentEvent.ConcurrencyStamp ?? Guid.Empty,
            Title = currentEvent.Title ?? string.Empty,
            Subtitle = currentEvent.Subtitle,
            Description = currentEvent.Description,
            Content = currentEvent.Content,
            AudienceGenderId = currentEvent.AudienceGenderId,
            AudienceAgeId = currentEvent.AudienceAgeId,
            FeaturedImageId = currentEvent.FeaturedImageId,
            ParticipationConfiguration = currentEvent.ParticipationConfiguration,
            RegistrationPolicyId = currentEvent.RegistrationPolicyId,
            EventTypeId = currentEvent.EventTypeId,
            EventFormatId = currentEvent.EventFormatId,
            VisibilityTypeId = currentEvent.VisibilityTypeId,
            MadhabId = currentEvent.MadhabId,
            Timezone = currentEvent.Timezone,
            BackgroundColor = currentEvent.BackgroundColor,
            BackgroundImageId = currentEvent.BackgroundImageId,
            BackgroundEffect = currentEvent.BackgroundEffect
        };

        _appearance = new AppearanceSettings
        {
            BackgroundColor = currentEvent.BackgroundColor ?? string.Empty,
            ImageUri = currentEvent.BackgroundImageUri ?? string.Empty,
            BackgroundEffect = currentEvent.BackgroundEffect ?? "None"
        };
        imagePreviewUrl = BuildFeaturedImagePreviewUrl(currentEvent);
        PublishMainContentAppearance();

        if (!string.IsNullOrEmpty(currentEvent.Timezone))
        {
            try
            {
                _selectedTimezone = TimeZoneInfo.FindSystemTimeZoneById(currentEvent.Timezone);
            }
            catch
            {
                _selectedTimezone = TimeZoneInfo.Utc;
            }
        }

        _editContext = new EditContext(updateDto);
        _errorStore.Init(_editContext);
    }

    private async Task OpenThemeStudioAsync()
    {
        await AccessibilityFocusService.SaveFocusAsync();
        _isThemeStudioOpen = true;
        await AccessibilityAnnouncerService.AnnouncePoliteAsync("Theme studio opened.");
    }

    private async Task CloseThemeStudioAsync()
    {
        _isThemeStudioOpen = false;
        await AccessibilityFocusService.RestoreFocusAsync(".create-event__theme-advanced-button");
        await AccessibilityAnnouncerService.AnnouncePoliteAsync("Theme studio closed.");
    }

    private Task SetBackgroundColorAsync(string value)
    {
        _appearance.BackgroundColor = value;
        if (updateDto is not null)
        {
            updateDto.BackgroundColor = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        PublishMainContentAppearance();
        return Task.CompletedTask;
    }

    private Task SetBackgroundEffectAsync(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "None" : value;
        _appearance.BackgroundEffect = normalized;
        if (updateDto is not null)
        {
            updateDto.BackgroundEffect = normalized == "None" ? null : normalized;
        }

        PublishMainContentAppearance();
        return Task.CompletedTask;
    }

    private string BuildEventEditPreviewStyle()
    {
        var settings = new AppearanceSettings
        {
            BackgroundColor = _appearance.BackgroundColor,
            BackgroundEffect = _appearance.BackgroundEffect,
            ImageUri = string.Empty
        };

        return settings.IsEmpty
            ? string.Empty
            : AppearanceStyleBuilder.BuildSurfaceStyle(settings, "#F8FAFC");
    }

    private void PublishMainContentAppearance()
    {
        MainContentAppearanceState?.Set(MainContentAppearanceOwner, BuildEventEditPreviewStyle());
    }

    private string? BuildFeaturedImagePreviewUrl(EventDto? eventDto)
    {
        if (!string.IsNullOrWhiteSpace(eventDto?.FeaturedImageUri))
        {
            return eventDto.FeaturedImageUri;
        }

        if (eventDto?.FeaturedImageId is not Guid imageId || imageId == Guid.Empty)
        {
            return null;
        }

        var baseUri = Navigation.BaseUri.TrimEnd('/');
        return $"{baseUri}/api/storageobject/{imageId}/public";
    }

    // ========== Image Upload ==========

    private async Task OnImageFileSelected(IBrowserFile? file)
    {
        _uploadError = null;

        if (file == null) return;

        if (!ImageUploadClientPolicy.IsAllowedImageContentType(file.ContentType))
        {
            _uploadError = ImageUploadClientPolicy.UnsupportedImageTypeMessage;
            return;
        }

        const long maxSize = ImageUploadClientPolicy.DefaultMaxImageFileSizeBytes;
        if (file.Size > maxSize)
        {
            _uploadError = ImageUploadClientPolicy.FormatMaxFileSizeMessage(maxSize);
            return;
        }

        _isUploadingImage = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var fileData = await ImageStorageService.ReadFileAsync(file, maxSize);
            if (fileData == null)
            {
                _uploadError = ImageUploadClientPolicy.ReadFailureMessage;
                return;
            }

            var preview = ImageStorageService.GenerateLocalPreviewFromBytes(fileData);
            if (!string.IsNullOrEmpty(preview))
            {
                imagePreviewUrl = preview;
            }

            var uploadResult = await ImageStorageService.UploadAndCreateRecordFromBytesAsync(fileData);
            if (uploadResult?.Success == true)
            {
                _uploadedImageStorageObjectId = uploadResult.StorageObjectId;
                _uploadError = null;
            }
            else
            {
                _uploadError = ImageUploadClientPolicy.ToUserSafeUploadError(uploadResult?.ErrorMessage);
                imagePreviewUrl = BuildFeaturedImagePreviewUrl(currentEvent);
                _uploadedImageStorageObjectId = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                "Image upload failed. FailureType={FailureType}, SizeBucket={SizeBucket}",
                ImageUploadClientPolicy.GetFailureType(ex),
                ImageUploadClientPolicy.GetSizeBucket(file.Size));
            _uploadError = ImageUploadClientPolicy.GenericUploadFailureMessage;
            imagePreviewUrl = BuildFeaturedImagePreviewUrl(currentEvent);
            _uploadedImageStorageObjectId = null;
        }
        finally
        {
            _isUploadingImage = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ========== Content Dialog ==========

    private async Task OpenDescriptionDialog()
    {
        if (updateDto == null) return;

        var parameters = new DialogParameters<DescriptionDialog>
        {
            { x => x.Content, updateDto.Content }
        };

        var options = DialogOptionsFactory.Editor();

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await DialogService.ShowAsync<DescriptionDialog>("", parameters, options);
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result is not null && !result.Canceled)
        {
            updateDto.Content = result.Data?.ToString();
        }
    }

    // ========== Session Management ==========

    private async Task AddSession()
    {
        if (!CanAddSession || EventId == Guid.Empty)
        {
            _submitState.Fail("You do not currently have permission to add sessions to this event.");
            return;
        }

        await SaveEventAsync($"/events/{EventId}/sessions/create");
    }

    private void EditSession(int index)
    {
        if (index < 0 || index >= sessions.Count || sessions[index].Id is not { } sessionId || sessionId == Guid.Empty)
        {
            _submitState.Fail("Save the session before editing it in the dedicated composer.");
            return;
        }

        Navigation.NavigateTo($"/events/{EventId}/sessions/{sessionId}/edit");
    }

    private void ShowDuplicateUnavailable()
    {
        _submitState.Fail("Duplicate session will be available from the dedicated session composer.");
    }

    private void ShowProgramSectionUnavailable()
    {
        _submitState.Fail("Section and track setup will be available from the saved event program manager.");
    }

    private async Task OpenProgramSectionsDialogAsync()
    {
        if (!CanManageProgramSections || EventId == Guid.Empty)
        {
            _submitState.Fail("You do not currently have permission to manage program sections for this event.");
            return;
        }

        var parameters = new DialogParameters<ProgramSectionsDialog>
        {
            { x => x.EventId, EventId },
            { x => x.InitialSections, _programSections }
        };

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await ProgramSectionsDialog.ShowAsync(
            DialogService,
            "Program sections",
            parameters,
            DialogOptionsFactory.Medium());
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result is null || result.Canceled || result.Data is not true)
            return;

        await RefreshProgramSectionsAsync();
    }

    private async Task RefreshProgramSectionsAsync()
    {
        _programSections = (await EventSessionService.GetManagedSessionGroupsByEventAsync(EventId)).ToList();
        _programSummary = await EventService.GetManagedEventProgramSummaryAsync(EventId);
    }

    private async void RemoveSession(int index)
    {
        if (index >= 0 && index < sessions.Count)
        {
            if (sessions.Count <= 1)
            {
                _submitState.Fail("You must have at least one session.");
                return;
            }

            var session = sessions[index];

            if (session.Id.HasValue && session.Id != Guid.Empty)
            {
                await AccessibilityFocusService.SaveFocusAsync();
                bool? confirm = await DialogService.ShowMessageBoxAsync(
                    "Delete Session",
                    "This session already exists. Deleting it here will remove it permanently. Continue?",
                    yesText: "Delete", cancelText: "Cancel");
                await AccessibilityFocusService.RestoreFocusAsync();

                if (confirm == true)
                {
                    try
                    {
                        await EventSessionService.DeleteSessionAsync(session.Id.Value);
                        sessions.RemoveAt(index);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to delete session {EventSessionId} while editing event {EventId}", session.Id.Value, EventId);
                        _submitState.Fail("Session could not be deleted. Please try again.");
                    }
                }
            }
            else
            {
                sessions.RemoveAt(index);
            }
        }
    }

    private async Task LoadCustomPropertyDefinitionsAsync()
    {
        _customPropertyDefinitionLoadError = null;
        _sessionCustomPropertyDefinitionLoadErrors = new Dictionary<Guid, string>();

        try
        {
            _eventCustomPropertyDefinitions = await CustomPropertyDefinitionService.GetEventDefinitionsAsync(EventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load runtime custom-property definitions for event {EventId}", EventId);
            _eventCustomPropertyDefinitions = Array.Empty<CustomPropertyDefinitionDto>();
            _customPropertyDefinitionLoadError = "Custom property definitions could not be loaded. Refresh before editing custom fields.";
        }

        var persistedSessionIds = sessions
            .Select(s => s.Id)
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id.GetValueOrDefault())
            .Distinct()
            .ToList();

        var definitionTasks = persistedSessionIds.Select(async sessionId =>
        {
            IReadOnlyList<CustomPropertyDefinitionDto> definitions;
            string? loadError = null;
            try
            {
                definitions = await CustomPropertyDefinitionService.GetEventSessionDefinitionsAsync(sessionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load runtime custom-property definitions for event session {EventSessionId}", sessionId);
                loadError = "Session custom property definitions could not be loaded. Refresh before editing custom fields.";
                definitions = Array.Empty<CustomPropertyDefinitionDto>();
            }

            return (SessionId: sessionId, Definitions: definitions, LoadError: loadError);
        });

        var results = await Task.WhenAll(definitionTasks);
        _sessionCustomPropertyDefinitions = results.ToDictionary(
            result => result.SessionId,
            result => result.Definitions);
        _sessionCustomPropertyDefinitionLoadErrors = results
            .Where(result => !string.IsNullOrWhiteSpace(result.LoadError))
            .ToDictionary(result => result.SessionId, result => result.LoadError!);
    }

    private IReadOnlyList<CustomPropertyDefinitionDto> GetSessionCustomPropertyDefinitions(Guid? sessionId)
    {
        if (!sessionId.HasValue || sessionId.Value == Guid.Empty)
        {
            return Array.Empty<CustomPropertyDefinitionDto>();
        }

        return _sessionCustomPropertyDefinitions.TryGetValue(sessionId.Value, out var definitions)
            ? definitions
            : Array.Empty<CustomPropertyDefinitionDto>();
    }

    private string? GetSessionCustomPropertyDefinitionLoadError(Guid? sessionId)
    {
        if (!sessionId.HasValue || sessionId.Value == Guid.Empty)
        {
            return null;
        }

        return _sessionCustomPropertyDefinitionLoadErrors.TryGetValue(sessionId.Value, out var error)
            ? error
            : null;
    }

    // ========== Validation & Submission ==========

    private async Task HandleSubmit()
    {
        await SaveEventAsync($"/events/{EventId}");
    }

    private async Task<bool> SaveEventAsync(string? navigateToOnSuccess)
    {
        if (updateDto == null) return false;

        if (_isUploadingImage)
        {
            _submitState.Fail("Please wait for the image upload to complete.");
            return false;
        }

        if (_submitState.IsSubmitting) return false;
        _submitState.Start();

        try
        {
            if (_uploadedImageStorageObjectId.HasValue)
            {
                updateDto.FeaturedImageId = _uploadedImageStorageObjectId.Value;
            }

            updateDto.BackgroundColor = string.IsNullOrWhiteSpace(_appearance.BackgroundColor) ? null : _appearance.BackgroundColor;
            updateDto.BackgroundEffect = string.IsNullOrWhiteSpace(_appearance.BackgroundEffect) || _appearance.BackgroundEffect == "None" ? null : _appearance.BackgroundEffect;
            updateDto.Timezone = _selectedTimezone.Id;

            if (EventId == Guid.Empty)
            {
                _submitState.Fail("Event ID is missing");
                return false;
            }

            var response = await EventService.UpdateEventAsync(EventId, updateDto);

            if (response?.Success == true)
            {
                currentEvent = await EventService.GetEventByIdAsync(EventId) ?? currentEvent;
                if (currentEvent is not null)
                {
                    updateDto.ExpectedConcurrencyStamp = currentEvent.ConcurrencyStamp ?? Guid.Empty;
                }

                if (!string.IsNullOrWhiteSpace(navigateToOnSuccess))
                {
                    Navigation.NavigateTo(navigateToOnSuccess);
                }

                _submitState.Complete();
                return true;
            }
            else
            {
                var errorMsg = response?.Message ?? "Failed to update event.";
                if (response?.Errors != null && response.Errors.Any())
                {
                    errorMsg += " Errors: " + string.Join(", ", response.Errors);
                }
                _submitState.Fail(errorMsg);
                return false;
            }
        }
        catch (ApiException ex)
        {
            if (!_errorStore.HandleApiError(ex))
            {
                Logger.LogError(ex, "Exception during event update");
                _submitState.Fail("Event could not be updated. Please try again.");
            }
            else
            {
                _submitState.Fail("Please fix the validation errors below.");
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during event update");
            _submitState.Fail("Event could not be updated. Please try again.");
            return false;
        }
    }

    // ========== Timezone Methods ==========

    private Task<IEnumerable<TimeZoneInfo>> SearchTimezones(string? searchText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Task.FromResult(_allTimezones.AsEnumerable());
        }

        var results = _allTimezones
            .Where(tz => tz.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                      || tz.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                      || tz.StandardName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .AsEnumerable();

        return Task.FromResult(results);
    }

    private void OnTimezoneChanged(TimeZoneInfo? tz)
    {
        if (tz != null)
        {
            _selectedTimezone = tz;
        }
        _showTimezoneSelector = false;
    }

    private static string FormatTimezoneShort(TimeZoneInfo tz)
    {
        var offset = tz.BaseUtcOffset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var abs = offset.Duration();
        return $"GMT{sign}{abs.Hours}:{abs.Minutes:D2} {tz.StandardName}";
    }

    private void OpenCategoryManager()
    {
        _editTagCatMode = TagCategoryMode.Categories;
        _editTagCatInitialIds = selectedCategoryIds.ToList().AsReadOnly();
        _showEditTagCatPopup = true;
    }

    private void OpenTagManager()
    {
        _editTagCatMode = TagCategoryMode.Tags;
        _editTagCatInitialIds = selectedTagIds.ToList().AsReadOnly();
        _showEditTagCatPopup = true;
    }

    private void HandleEditTagCatSaved(IReadOnlyCollection<Guid> newIds)
    {
        if (_editTagCatMode == TagCategoryMode.Tags)
            selectedTagIds = new HashSet<Guid>(newIds);
        else
            selectedCategoryIds = new HashSet<Guid>(newIds);

        StateHasChanged();
    }

    public void Dispose()
    {
        MainContentAppearanceState?.Clear(MainContentAppearanceOwner);
    }

}
