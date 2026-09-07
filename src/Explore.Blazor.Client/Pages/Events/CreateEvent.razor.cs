using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Forms;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventTemplates;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events;

public partial class CreateEvent : IDisposable
{
    private const string MainContentAppearanceOwner = nameof(CreateEvent);
    private const int InformationOnly = 1;
    private const int WalkIn = 2;
    private const int ExternalManaged = 3;
    private const int PlatformManaged = 4;
    private const int NotApplicable = 1;
    private const int Optional = 2;
    private const int Required = 3;
    private const int AccountRequired = 1;
    private const int GuestAllowed = 2;
    private const int CapabilityTokenAllowed = 3;
    private const int VerifiedEmailRequired = 1;
    private const int UnverifiedEmailAccepted = 2;
    private const int EmailOptional = 3;
    private const int CapabilityLinkOnly = 4;
    private const int NoRecovery = 5;

    [Inject] protected IEventService EventService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.IEventSessionService EventSessionService { get; set; } = null!;
    [Inject] protected IUserService UserService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService EventLookupService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.IDemographicLookupService DemographicLookupService { get; set; } = null!;
    [Inject] protected Explore.Blazor.Client.Contracts.Services.Lookup.ICultureLookupService CultureLookupService { get; set; } = null!;
    [Inject] protected IImageStorageService ImageStorageService { get; set; } = null!;
    [Inject] protected ICategoryService CategoryService { get; set; } = null!;
    [Inject] protected ITagService TagService { get; set; } = null!;
    [Inject] protected IEventTemplateService EventTemplateService { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected IDialogService DialogService { get; set; } = null!;
    [Inject] protected ILogger<CreateEvent> Logger { get; set; } = null!;
    [Inject] private IAccessibilityFocusService AccessibilityFocusService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AccessibilityAnnouncerService { get; set; } = default!;
    [Inject] private IEventRegistrationPolicyService RegistrationPolicyService { get; set; } = default!;
    [Inject] private MainContentAppearanceState MainContentAppearanceState { get; set; } = default!;

    // Publisher selection state
    private string _publisherMode = "personal";
    private Guid? _selectedOrganizationId;
    private Guid? _selectedGroupId;
    private EventCreationContextDto? _creationContext;
    private string _creationContextError = string.Empty;

    // Form state
    private Guid? _currentUserId;
    private CreateEventDraftRequestDto createDto = new()
    {
        ParticipationConfiguration = new ConfigureEventParticipationDto
        {
            ParticipationHandlingModeId = InformationOnly,
            AdvanceRegistrationObligationId = NotApplicable
        }
    };
    private EditContext _editContext = default!;
    private FormSubmitState _submitState = new();
    private ServerValidationErrorStore _errorStore = new();
    private ICollection<EventTypeListDto>? eventTypes;
    private ICollection<AudienceGenderListDto>? audienceGenders;
    private ICollection<AudienceAgeListDto>? audienceAges;
    private ICollection<EventFormatListDto>? eventFormats;
    private ICollection<VisibilityTypeListDto>? visibilityTypes;
    private ICollection<MadhabListDto>? madhabs;
    private ICollection<CategoryListDto>? allCategories;
    private ICollection<TagListDto>? allTags;
    private ICollection<LocationListDto>? locations;
    private ICollection<RegistrationModeListDto>? registrationModes;
    private ICollection<LanguageListDto>? languages;
    private ICollection<EventRegistrationPolicyListDto>? registrationPolicies;
    private IReadOnlyList<HalResourceOfEventTemplateListDto> eventTemplates = Array.Empty<HalResourceOfEventTemplateListDto>();
    private HalResourceOfEventTemplateDto? _selectedEventTemplate;
    private bool _isLoadingEventTemplates;
    private bool _isLoadingTemplatePreview;
    private string? _templateLoadError;
    private int _templateListRequestVersion;
    private int _templatePreviewRequestVersion;
    private bool IsSubmitDisabled =>
        _submitState.IsSubmitting
        || _isUploadingImage
        || _isLoadingTemplatePreview
        || _creationContext is null
        || _creationContext.CanCreate != true;
    private bool isLoading = true;
    private bool _dataLoaded = false;
    private int? selectedMadhabId = null;
    private IReadOnlyList<EventPublishReadinessErrorDto> _publishReadinessErrors = Array.Empty<EventPublishReadinessErrorDto>();

    // Image upload state
    private string? imagePreviewUrl;
    private bool _isUploadingImage = false;
    private Guid? _uploadedImageStorageObjectId = null;
    private string? _uploadError;

    // Categories and Tags selection
    private IReadOnlyCollection<Guid> selectedCategoryIds = new HashSet<Guid>();
    private IReadOnlyCollection<Guid> selectedTagIds = new HashSet<Guid>();

    // Tag/Category management popup state
    private bool _showCreateTagCatPopup;
    private TagCategoryMode _createTagCatMode;
    private IReadOnlyCollection<Guid> _createTagCatInitialIds = Array.Empty<Guid>();

    private List<SessionEditorModel> sessions = new();
    private DateTime? _inlineSessionDate;
    private TimeSpan? _inlineSessionStartTime = TimeSpan.FromHours(9);
    private TimeSpan? _inlineSessionEndTime = TimeSpan.FromHours(17);
    private Guid? _inlineSessionLocationId;
    private int? _inlineSessionCapacity;
    private string _bgColor = string.Empty;
    private string _bgEffect = "None";
    private string _bgImageUri = string.Empty;

    // UI toggles
    private bool _showTimezoneSelector = false;
    private bool _isMoreOptionsExpanded;
    private bool _isThemeStudioOpen;
    private bool _isCustomSlugEnabled;
    private string _programAnnouncement = string.Empty;
    private string ProgramSummary => BuildProgramSummary();
    private string ProgramItemsSummary => BuildProgramItemsSummary();
    private string ProgramLogisticsSummary => BuildProgramLogisticsSummary();
    private string ProgramDefaultsSummary => BuildProgramDefaultsSummary();
    private string MoreOptionsSummary => BuildMoreOptionsSummary();
    private bool AreEventOptionsDisabled => _creationContext is not null && _creationContext.CanCreate != true;
    private string? EventOptionsPolicyNote => BuildEventOptionsPolicyNote();
    private string VisibilitySummary => GetLookupName(visibilityTypes, createDto.VisibilityTypeId) ?? "Select visibility";
    private string AudienceSummary => BuildAudienceSummary();
    private string RegistrationSummary => BuildRegistrationSummary();
    private string EventTypeSummary => GetLookupName(eventTypes, createDto.EventTypeId) ?? "Select event type";
    private string MadhabSummary => GetLookupName(madhabs, selectedMadhabId) ?? "No madhab set";
    private List<string> SelectedCategoryNames => GetSelectedNames(allCategories, selectedCategoryIds);
    private List<string> SelectedTagNames => GetSelectedNames(allTags, selectedTagIds);
    private string CategoriesSummary => SelectedCategoryNames.Count == 0 ? "No categories selected" : string.Join(", ", SelectedCategoryNames);
    private string TagsSummary => SelectedTagNames.Count == 0 ? "No tags selected" : string.Join(", ", SelectedTagNames);
    private bool IsNoAdvanceParticipationMode =>
        createDto.ParticipationConfiguration.ParticipationHandlingModeId is InformationOnly or WalkIn;
    private bool IsPlatformManagedParticipation =>
        createDto.ParticipationConfiguration.ParticipationHandlingModeId == PlatformManaged;
    private bool UsesEmailParticipationRecovery =>
        IsPlatformManagedParticipation && createDto.ParticipationConfiguration.IdentityAccessModeId == GuestAllowed;
    private bool UsesCapabilityParticipationRecovery =>
        IsPlatformManagedParticipation && createDto.ParticipationConfiguration.IdentityAccessModeId == CapabilityTokenAllowed;
    // Timezone
    private TimeZoneInfo _selectedTimezone = TimeZoneInfo.Utc;
    private string _selectedTimezoneDisplay => FormatTimezoneShort(_selectedTimezone);
    private static readonly IReadOnlyList<TimeZoneInfo> _allTimezones = TimeZoneInfo.GetSystemTimeZones();
    private Guid createdEventId = Guid.Empty;

    private string BuildProgramSummary()
    {
        var sessionLabel = sessions.Count switch
        {
            0 => "No sessions prepared yet",
            1 => "1 session prepared",
            _ => $"{sessions.Count} sessions prepared"
        };

        return $"{sessionLabel}; logistics are managed after the draft is saved.";
    }

    private string BuildProgramItemsSummary()
    {
        if (sessions.Count == 0)
        {
            return "Add talks, workshops, panels, classes, or activities as sessions.";
        }

        var firstTitle = sessions.First().Title?.Trim();
        var firstSession = string.IsNullOrWhiteSpace(firstTitle)
            ? "first session"
            : firstTitle;

        return sessions.Count == 1
            ? $"1 session prepared: {firstSession}"
            : $"{sessions.Count} sessions prepared, starting with {firstSession}";
    }

    private string BuildProgramLogisticsSummary()
    {
        return "Breaks, rooms, meals, prayer times, and day details are managed after the draft is saved.";
    }

    private string BuildProgramDefaultsSummary()
    {
        return $"{RegistrationSummary} · Session-specific defaults move to the session composer";
    }

    private string BuildMoreOptionsSummary()
    {
        var selectedCount = 0;
        if (createDto.TemplateId.HasValue)
        {
            selectedCount++;
        }

        return selectedCount == 0
            ? "Template and custom fields are optional."
            : $"{selectedCount} advanced setting{(selectedCount == 1 ? string.Empty : "s")} selected.";
    }

    private string? BuildEventOptionsPolicyNote()
    {
        if (_creationContext is null)
        {
            return null;
        }

        if (_creationContext.CanCreate != true)
        {
            return _creationContext.UnavailableReason ?? "Creation is currently unavailable for your account.";
        }

        return _creationContext.RequiresApproval == true
            ? "This publisher requires approval before the event becomes public. Required settings remain visible so review blockers are clear."
            : null;
    }

    private string BuildAudienceSummary()
    {
        var gender = GetLookupName(audienceGenders, createDto.AudienceGenderId) ?? "Any gender";
        var age = GetLookupName(audienceAges, createDto.AudienceAgeId) ?? "Any age";
        return $"{gender} · {age}";
    }

    private string BuildRegistrationSummary()
    {
        var configuration = createDto.ParticipationConfiguration;
        var handling = configuration.ParticipationHandlingModeId switch
        {
            InformationOnly => "Information only",
            WalkIn => "Walk-in",
            ExternalManaged => "Externally managed",
            PlatformManaged => "Platform managed",
            _ => "Select participation handling"
        };
        var advance = configuration.AdvanceRegistrationObligationId switch
        {
            NotApplicable => "Not applicable",
            Optional => "Optional advance registration",
            Required => "Required advance registration",
            _ => "Select advance registration"
        };
        var identity = configuration.IdentityAccessModeId switch
        {
            AccountRequired => "Account required",
            GuestAllowed => "Guests allowed",
            CapabilityTokenAllowed => "Capability-token access",
            _ => null
        };
        var recovery = ((int?)configuration.GuestRecoveryPolicy) switch
        {
            VerifiedEmailRequired => "Verified email recovery",
            UnverifiedEmailAccepted => "Unverified email recovery",
            EmailOptional => "Optional email recovery",
            CapabilityLinkOnly => "Capability-link recovery",
            NoRecovery => "No recovery",
            _ => null
        };
        var policy = GetLookupName(registrationPolicies, createDto.RegistrationPolicyId) ?? "Default open registration";
        return string.Join(" · ", new[] { handling, advance, identity, recovery, policy, "Capacity set per session" }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private Task OnParticipationHandlingModeChanged(int? value)
    {
        if (!value.HasValue)
        {
            return Task.CompletedTask;
        }

        var configuration = createDto.ParticipationConfiguration;
        configuration.ParticipationHandlingModeId = value.Value;
        switch (value.Value)
        {
            case InformationOnly:
            case WalkIn:
                configuration.AdvanceRegistrationObligationId = NotApplicable;
                configuration.IdentityAccessModeId = null;
                configuration.GuestRecoveryPolicy = null;
                break;
            case ExternalManaged:
                configuration.AdvanceRegistrationObligationId = configuration.AdvanceRegistrationObligationId is Optional or Required
                    ? configuration.AdvanceRegistrationObligationId
                    : Optional;
                configuration.IdentityAccessModeId = null;
                configuration.GuestRecoveryPolicy = null;
                break;
            case PlatformManaged:
                configuration.AdvanceRegistrationObligationId = configuration.AdvanceRegistrationObligationId is Optional or Required
                    ? configuration.AdvanceRegistrationObligationId
                    : Optional;
                break;
        }

        return Task.CompletedTask;
    }

    private Task OnParticipationAdvanceObligationChanged(int? value)
    {
        if (value.HasValue)
        {
            createDto.ParticipationConfiguration.AdvanceRegistrationObligationId = value.Value;
        }

        return Task.CompletedTask;
    }

    private Task OnParticipationIdentityAccessModeChanged(int? value)
    {
        var configuration = createDto.ParticipationConfiguration;
        configuration.IdentityAccessModeId = value;
        configuration.GuestRecoveryPolicy = value switch
        {
            AccountRequired => null,
            GuestAllowed when ((int?)configuration.GuestRecoveryPolicy) is VerifiedEmailRequired or UnverifiedEmailAccepted or EmailOptional => configuration.GuestRecoveryPolicy,
            CapabilityTokenAllowed when ((int?)configuration.GuestRecoveryPolicy) is CapabilityLinkOnly or NoRecovery => configuration.GuestRecoveryPolicy,
            _ => null
        };
        return Task.CompletedTask;
    }

    private Task OnParticipationGuestRecoveryPolicyChanged(int? value)
    {
        createDto.ParticipationConfiguration.GuestRecoveryPolicy = (GuestRecoveryPolicyEnum?)value;
        return Task.CompletedTask;
    }

    private bool IsLegalParticipationConfiguration()
    {
        var configuration = createDto.ParticipationConfiguration;
        return configuration.ParticipationHandlingModeId switch
        {
            InformationOnly or WalkIn => configuration.AdvanceRegistrationObligationId == NotApplicable
                && configuration.IdentityAccessModeId is null
                && configuration.GuestRecoveryPolicy is null,
            ExternalManaged => configuration.AdvanceRegistrationObligationId is Optional or Required
                && configuration.IdentityAccessModeId is null
                && configuration.GuestRecoveryPolicy is null,
            PlatformManaged => configuration.AdvanceRegistrationObligationId is Optional or Required
                && configuration.IdentityAccessModeId switch
                {
                    AccountRequired => configuration.GuestRecoveryPolicy is null,
                    GuestAllowed => ((int?)configuration.GuestRecoveryPolicy) is VerifiedEmailRequired or UnverifiedEmailAccepted or EmailOptional,
                    CapabilityTokenAllowed => ((int?)configuration.GuestRecoveryPolicy) is CapabilityLinkOnly or NoRecovery,
                    _ => false
                },
            _ => false
        };
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

    private static string? GetLookupName(IEnumerable<LocationListDto>? items, Guid? selectedId) =>
        selectedId.HasValue
            ? items?.FirstOrDefault(item => item.Id == selectedId.Value)?.FullName
            : null;

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

    private void OpenCategoryManager()
    {
        if (AreEventOptionsDisabled)
        {
            return;
        }

        _createTagCatMode = TagCategoryMode.Categories;
        _createTagCatInitialIds = selectedCategoryIds.ToList().AsReadOnly();
        _showCreateTagCatPopup = true;
    }

    private void OpenTagManager()
    {
        if (AreEventOptionsDisabled)
        {
            return;
        }

        _createTagCatMode = TagCategoryMode.Tags;
        _createTagCatInitialIds = selectedTagIds.ToList().AsReadOnly();
        _showCreateTagCatPopup = true;
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
        _bgColor = value;
        PublishMainContentAppearance();
        return Task.CompletedTask;
    }

    private Task SetBackgroundEffectAsync(string value)
    {
        _bgEffect = string.IsNullOrWhiteSpace(value) ? "None" : value;
        PublishMainContentAppearance();
        return Task.CompletedTask;
    }

    private Task SetCustomSlugEnabledAsync(bool value)
    {
        _isCustomSlugEnabled = value;
        createDto.Slug = value ? EventUrlHelper.FormatSlug(createDto.Title) : null;
        return Task.CompletedTask;
    }

    private Task ApplyTitleSlugAsync()
    {
        if (_isCustomSlugEnabled)
            createDto.Slug = EventUrlHelper.FormatSlug(createDto.Title);
        else
            createDto.Slug = null;

        return Task.CompletedTask;
    }

    private string BuildCreateEventPreviewStyle()
    {
        var settings = new AppearanceSettings
        {
            BackgroundColor = _bgColor,
            ImageUri = _bgImageUri,
            BackgroundEffect = _bgEffect
        };

        return settings.IsEmpty
            ? string.Empty
            : AppearanceStyleBuilder.BuildSurfaceStyle(settings, "#F8FAFC");
    }

    private void PublishMainContentAppearance()
    {
        MainContentAppearanceState.Set(MainContentAppearanceOwner, BuildCreateEventPreviewStyle());
    }

    public void Dispose()
    {
        MainContentAppearanceState.Clear(MainContentAppearanceOwner);
    }

    protected override async Task OnInitializedAsync()
    {
        _editContext = new EditContext(createDto);
        _errorStore.Init(_editContext);

        Logger.LogInformation("OnInitializedAsync starting");
        await LoadFormData();
        PublishMainContentAppearance();

    }

    // ========== Publisher Methods ==========

    private string SelectedPublisherKey => BuildPublisherKey(_publisherMode, _publisherMode switch
    {
        "organization" => _selectedOrganizationId,
        "group" => _selectedGroupId,
        _ => null
    });

    private IReadOnlyList<PublisherChoice> GetPublisherChoices()
    {
        if (_creationContext?.PublisherOptions is { Count: > 0 } options)
        {
            return options
                .Select(option => new PublisherChoice(
                    BuildPublisherKey(option.PublisherMode, option.PublisherId),
                    option.PublisherMode,
                    option.PublisherId,
                    option.CanPublish == true,
                    option.DisplayName ?? GetPublisherModeLabel(option.PublisherMode),
                    option.CanPublish == true
                        ? GetPublisherModeLabel(option.PublisherMode)
                        : option.Reason ?? "This publisher cannot create events."))
                .ToList();
        }

        return [];
    }

    private void OnPublisherSelectionChanged(string? key)
    {
        if (!TryParsePublisherKey(key, out var mode, out var publisherId))
        {
            return;
        }

        if (_creationContext is null || GetPublisherOption(mode, publisherId)?.CanPublish != true)
        {
            return;
        }

        _publisherMode = mode;
        _selectedOrganizationId = mode == "organization" ? publisherId : null;
        _selectedGroupId = mode == "group" ? publisherId : null;
    }

    private static string BuildPublisherKey(string? mode, Guid? publisherId) =>
        $"{mode ?? string.Empty}:{publisherId?.ToString("D") ?? string.Empty}";

    private static bool TryParsePublisherKey(string? key, out string mode, out Guid? publisherId)
    {
        mode = string.Empty;
        publisherId = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var separatorIndex = key.IndexOf(':', StringComparison.Ordinal);
        mode = separatorIndex < 0 ? key : key[..separatorIndex];
        var rawId = separatorIndex < 0 ? string.Empty : key[(separatorIndex + 1)..];
        if (!string.IsNullOrWhiteSpace(rawId) && Guid.TryParse(rawId, out var parsedId))
        {
            publisherId = parsedId;
        }

        return !string.IsNullOrWhiteSpace(mode);
    }

    private static string GetPublisherModeLabel(string? mode) => mode switch
    {
        "personal" => "Personal profile",
        "organization" => "Organization",
        "group" => "Group",
        _ => "Publisher"
    };

    private static string GetPublisherIcon(string? mode) => mode switch
    {
        "organization" => Icons.Material.Filled.Business,
        "group" => Icons.Material.Filled.Group,
        _ => Icons.Material.Filled.Person
    };

    private sealed record PublisherChoice(
        string Key,
        string Mode,
        Guid? PublisherId,
        bool CanPublish,
        string DisplayName,
        string Description);

    private async Task OnEventTypeChanged(int? eventTypeId)
    {
        if (createDto.EventTypeId == eventTypeId)
        {
            return;
        }

        createDto.EventTypeId = eventTypeId;
        createDto.TemplateId = null;
        _selectedEventTemplate = null;
        eventTemplates = Array.Empty<HalResourceOfEventTemplateListDto>();
        ClearSessionTemplateSelections();
        await LoadEventTemplatesAsync(eventTypeId);
    }

    private async Task OnEventTemplateChanged(Guid? templateId)
    {
        var previewRequestVersion = ++_templatePreviewRequestVersion;
        createDto.TemplateId = templateId;
        _selectedEventTemplate = null;
        _templateLoadError = null;
        ClearSessionTemplateSelections();

        if (!templateId.HasValue || templateId.Value == Guid.Empty)
        {
            _isLoadingTemplatePreview = false;
            return;
        }

        var requestedTemplateId = templateId.Value;
        _isLoadingTemplatePreview = true;
        try
        {
            var template = await EventTemplateService.GetTemplateByIdAsync(requestedTemplateId);
            if (!IsCurrentTemplatePreviewRequest(previewRequestVersion, requestedTemplateId))
            {
                return;
            }

            if (template is null)
            {
                createDto.TemplateId = null;
                _templateLoadError = "The selected template could not be loaded. The selection was cleared; choose another template or continue without one.";
                return;
            }

            _selectedEventTemplate = template;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load event template preview for {TemplateId}", requestedTemplateId);
            if (IsCurrentTemplatePreviewRequest(previewRequestVersion, requestedTemplateId))
            {
                createDto.TemplateId = null;
                _templateLoadError = "Template preview could not be loaded. The selection was cleared; choose another template or continue without one.";
            }
        }
        finally
        {
            if (IsCurrentTemplatePreviewRequest(previewRequestVersion, requestedTemplateId) || !createDto.TemplateId.HasValue)
            {
                _isLoadingTemplatePreview = false;
            }
        }
    }

    private bool IsCurrentTemplatePreviewRequest(int requestVersion, Guid requestedTemplateId) =>
        requestVersion == _templatePreviewRequestVersion && createDto.TemplateId == requestedTemplateId;

    private void ClearSessionTemplateSelections()
    {
        foreach (var session in sessions)
        {
            session.SessionTemplateId = null;
        }

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
            Logger.LogInformation(
                "[CreateEvent] Reading selected image. SizeBucket={SizeBucket}, ContentTypeBucket={ContentTypeBucket}",
                ImageUploadClientPolicy.GetSizeBucket(file.Size),
                ImageUploadClientPolicy.GetContentTypeBucket(file.ContentType));
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

            Logger.LogInformation("[CreateEvent] Uploading image...");
            var uploadResult = await ImageStorageService.UploadAndCreateRecordFromBytesAsync(fileData);
            if (uploadResult?.Success == true)
            {
                _uploadedImageStorageObjectId = uploadResult.StorageObjectId;
                _uploadError = null;
                Logger.LogInformation("[CreateEvent] Image uploaded. StorageObjectId: {Id}", uploadResult.StorageObjectId);
            }
            else
            {
                _uploadError = ImageUploadClientPolicy.ToUserSafeUploadError(uploadResult?.ErrorMessage);
                imagePreviewUrl = null;
                _uploadedImageStorageObjectId = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                "[CreateEvent] Image upload failed. FailureType={FailureType}, SizeBucket={SizeBucket}",
                ImageUploadClientPolicy.GetFailureType(ex),
                ImageUploadClientPolicy.GetSizeBucket(file.Size));
            _uploadError = ImageUploadClientPolicy.GenericUploadFailureMessage;
            imagePreviewUrl = null;
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
        var parameters = new DialogParameters<DescriptionDialog>
        {
            { x => x.Content, createDto.Content }
        };

        var options = DialogOptionsFactory.Editor();

        await AccessibilityFocusService.SaveFocusAsync();
        var dialog = await DialogService.ShowAsync<DescriptionDialog>("", parameters, options);
        var result = await dialog.Result;
        await AccessibilityFocusService.RestoreFocusAsync();

        if (result is not null && !result.Canceled)
        {
            createDto.Content = result.Data?.ToString();
        }
    }

    // ========== Session Management ==========

    private async Task AddSessionAsync()
    {
        AnnounceProgramChange("Saving this event as a draft before opening session management.");
        await SubmitEventAsync(CreateEventSubmitIntent.SaveDraftAndAddSession);
    }

    private async Task OpenDedicatedSessionComposerAsync(int _)
    {
        AnnounceProgramChange("Saving this event as a draft before opening the dedicated session composer.");
        await SubmitEventAsync(CreateEventSubmitIntent.SaveDraftAndAddSession);
    }

    private void ShowDuplicateSessionUnavailable(int _)
    {
        AnnounceProgramChange("Duplicate session will be available from the dedicated session composer.");
    }

    private void ShowProgramSectionUnavailable()
    {
        AnnounceProgramChange("Section and track setup will open from the saved event program manager.");
    }

    private void AnnounceProgramChange(string message)
    {
        _programAnnouncement = message;
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
                        Logger.LogError(ex, "Failed to delete session {EventSessionId} while editing draft event", session.Id.Value);
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

    // ========== Form Data Loading ==========

    private async Task LoadFormData()
    {
        if (_dataLoaded) return;

        try
        {
            isLoading = true;
            Logger.LogInformation("Loading form data for create event page");

            if (!_currentUserId.HasValue)
            {
                try
                {
                    var currentUser = await UserService.GetCurrentUserAsync();
                    if (currentUser != null)
                    {
                        _currentUserId = currentUser.Id;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error getting current user");
                }
            }

            await LoadCreationContextAsync();

            var eventTypesTask = EventLookupService.GetEventTypesAsync();
            var audienceGendersTask = DemographicLookupService.GetAudienceGendersAsync();
            var audienceAgesTask = DemographicLookupService.GetAudienceAgesAsync();
            var eventFormatsTask = EventLookupService.GetEventFormatsAsync();
            var visibilityTypesTask = EventLookupService.GetVisibilityTypesAsync();
            var madhabsTask = CultureLookupService.GetMadhabsAsync();
            var categoriesTask = CategoryService.GetAllCategoriesAsync();
            var tagsTask = TagService.GetAllTagsAsync();
            var registrationModesTask = EventLookupService.GetRegistrationModesAsync();
            var languagesTask = CultureLookupService.GetLanguagesAsync();
            var registrationPoliciesTask = RegistrationPolicyService.GetEventRegistrationPoliciesAsync();
            var eventTemplatesTask = LoadEventTemplatesAsync(createDto.EventTypeId);

            await Task.WhenAll(eventTypesTask, audienceGendersTask, audienceAgesTask, eventFormatsTask, visibilityTypesTask, madhabsTask, categoriesTask, tagsTask, registrationModesTask, languagesTask, registrationPoliciesTask, eventTemplatesTask);

            eventTypes = await eventTypesTask;
            audienceGenders = await audienceGendersTask;
            audienceAges = await audienceAgesTask;
            eventFormats = await eventFormatsTask;
            visibilityTypes = await visibilityTypesTask;
            madhabs = await madhabsTask;
            allCategories = await categoriesTask;
            allTags = await tagsTask;
            registrationModes = await registrationModesTask;
            languages = await languagesTask;
            registrationPolicies = await registrationPoliciesTask;

            SetDefaultValues();
            _dataLoaded = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading form data");
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadEventTemplatesAsync(int? eventTypeId)
    {
        var requestVersion = ++_templateListRequestVersion;
        _templateLoadError = null;
        _isLoadingEventTemplates = true;
        try
        {
            var templates = await EventTemplateService.GetTemplatesAsync(eventTypeId, pageNumber: 1, pageSize: 100);
            if (!IsCurrentTemplateListRequest(requestVersion, eventTypeId))
            {
                return;
            }

            eventTemplates = (templates._embedded?.Items ?? [])
                .Where(t => t.IsActive == true && t.IsPublished == true)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.DisplayName)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load event templates for event type {EventTypeId}", eventTypeId);
            if (IsCurrentTemplateListRequest(requestVersion, eventTypeId))
            {
                eventTemplates = Array.Empty<HalResourceOfEventTemplateListDto>();
                _templateLoadError = "Event templates could not be loaded. You can still create a vanilla event.";
            }
        }
        finally
        {
            if (IsCurrentTemplateListRequest(requestVersion, eventTypeId))
            {
                _isLoadingEventTemplates = false;
            }
        }
    }

    private bool IsCurrentTemplateListRequest(int requestVersion, int? requestedEventTypeId) =>
        requestVersion == _templateListRequestVersion && createDto.EventTypeId == requestedEventTypeId;

    private void SetDefaultValues()
    {
        if (eventFormats?.Any() == true && createDto.EventFormatId is null or <= 0)
        {
            createDto.EventFormatId = eventFormats.First().Id;
        }
        if (visibilityTypes?.Any() == true && createDto.VisibilityTypeId is null or <= 0)
        {
            createDto.VisibilityTypeId = visibilityTypes.First().Id;
        }
        _inlineSessionDate ??= TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _selectedTimezone).Date.AddDays(1);
    }

    // ========== Validation & Submission ==========

    private bool ValidateForm()
    {
        _submitState.Reset();

        if (_creationContext is null)
        {
            _submitState.Fail(_creationContextError is { Length: > 0 }
                ? _creationContextError
                : "Creation permissions could not be loaded. Publishing is unavailable.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(createDto.Title))
        {
            _submitState.Fail("Event name is required.");
            return false;
        }

        if (_creationContext.CanCreate != true)
        {
            _submitState.Fail(_creationContext.UnavailableReason ?? "You do not have access to create events.");
            return false;
        }

        if (!CanSelectPublisherMode(_publisherMode))
        {
            _submitState.Fail("Select an available publisher before creating the event.");
            return false;
        }

        if (_publisherMode == "organization")
        {
            if (!_selectedOrganizationId.HasValue)
            {
                _submitState.Fail("Please select an organization.");
                return false;
            }
            if (GetPublisherOption("organization", _selectedOrganizationId.Value)?.CanPublish != true)
            {
                _submitState.Fail("You cannot publish events for the selected organization.");
                return false;
            }
        }

        if (_publisherMode == "group")
        {
            if (!_selectedGroupId.HasValue)
            {
                _submitState.Fail("Please select a group.");
                return false;
            }
            if (GetPublisherOption("group", _selectedGroupId.Value)?.CanPublish != true)
            {
                _submitState.Fail("You cannot publish events for the selected group.");
                return false;
            }
        }

        if (!IsLegalParticipationConfiguration())
        {
            _submitState.Fail("Choose a valid participation configuration before creating the event.");
            return false;
        }

        return true;
    }

    private async Task LoadCreationContextAsync()
    {
        _creationContextError = string.Empty;

        try
        {
            _creationContext = await EventService.GetEventCreationContextAsync();
            if (_creationContext is null)
            {
                _creationContextError = "Creation permissions could not be loaded. Publishing is unavailable.";
                return;
            }

            ApplyCreationContextDefaults();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading event creation context");
            _creationContext = null;
            _creationContextError = "Creation permissions could not be loaded. Publishing is unavailable.";
        }
    }

    private void ApplyCreationContextDefaults()
    {
        if (_creationContext is null)
        {
            return;
        }

        if (_creationContext.CanCreate != true)
        {
            _publisherMode = "personal";
            _selectedOrganizationId = null;
            _selectedGroupId = null;
            return;
        }

        var defaultMode = _creationContext.DefaultPublisherMode;
        if (string.IsNullOrWhiteSpace(defaultMode) || !CanSelectPublisherMode(defaultMode))
        {
            defaultMode = GetFirstPublishableOption()?.PublisherMode;
        }

        if (string.IsNullOrWhiteSpace(defaultMode))
        {
            return;
        }

        _publisherMode = defaultMode;
        _selectedOrganizationId = null;
        _selectedGroupId = null;
        if (_publisherMode == "organization")
        {
            _selectedOrganizationId = GetFirstPublishableOption("organization")?.PublisherId;
        }
        else if (_publisherMode == "group")
        {
            _selectedGroupId = GetFirstPublishableOption("group")?.PublisherId;
        }
    }

    private EventCreationPublisherOptionDto? GetFirstPublishableOption(string? mode = null) =>
        _creationContext?.PublisherOptions?
            .FirstOrDefault(option => option.CanPublish == true && (mode is null || option.PublisherMode == mode));

    private EventCreationPublisherOptionDto? GetPublisherOption(string mode, Guid? publisherId) =>
        _creationContext?.PublisherOptions?
            .FirstOrDefault(option => option.PublisherMode == mode && option.PublisherId == publisherId);

    private bool CanSelectPublisherMode(string mode)
    {
        return mode switch
        {
            "personal" => GetPublisherOption("personal", null)?.CanPublish == true,
            "organization" => GetFirstPublishableOption("organization") is not null,
            "group" => GetFirstPublishableOption("group") is not null,
            _ => false
        };
    }

    private async Task HandleSubmit() => await SubmitEventAsync(CreateEventSubmitIntent.ReviewAndPublish);

    private async Task SubmitEventAsync(CreateEventSubmitIntent intent)
    {
        if (!ValidateForm())
        {
            return;
        }

        if (_isUploadingImage)
        {
            _submitState.Fail("Please wait for the image upload to complete.");
            return;
        }

        if (_isLoadingTemplatePreview)
        {
            _submitState.Fail("Please wait for the template preview to finish loading.");
            return;
        }

        if (_submitState.IsSubmitting) return;
        _submitState.Start();
        _publishReadinessErrors = Array.Empty<EventPublishReadinessErrorDto>();

        try
        {
            Guid? featuredImageId = _uploadedImageStorageObjectId;

            if (featuredImageId.HasValue)
            {
                Logger.LogInformation("Using pre-uploaded image. StorageObjectId: {StorageObjectId}", featuredImageId);
            }

            createDto.OrganizationId = _publisherMode == "organization" ? _selectedOrganizationId : null;
            createDto.GroupId = _publisherMode == "group" ? _selectedGroupId : null;
            createDto.FeaturedImageId = featuredImageId;
            createDto.MadhabId = selectedMadhabId;
            createDto.VisibilityTypeId ??= 1;
            createDto.EventFormatId ??= 1;
            createDto.EventStatusId = (intent == CreateEventSubmitIntent.ReviewAndPublish) ? 2 : 1;
            createDto.Timezone = _selectedTimezone.Id;
            createDto.BackgroundColor = string.IsNullOrWhiteSpace(_bgColor) ? null : _bgColor;
            createDto.BackgroundEffect = string.IsNullOrWhiteSpace(_bgEffect) || _bgEffect == "None" ? null : _bgEffect;

            PopulateCreateRequest();
            if (!ApplyInlineSessionForIntent(intent))
            {
                return;
            }

            Logger.LogInformation(
                "Creating event (publisherMode={Mode}, organizationId={OrgId}, groupId={GroupId})",
                _publisherMode,
                createDto.OrganizationId,
                createDto.GroupId);
            var response = await EventService.CreateEventAsync(createDto);

            if (response?.Success == true && response.Id != Guid.Empty)
            {
                _submitState.Complete();
                createdEventId = response.Id;
                Logger.LogInformation("Event created with ID: {EventId}", createdEventId);

                if (intent == CreateEventSubmitIntent.SaveDraft)
                {
                    Navigation.NavigateTo($"/events/{createdEventId}/edit");
                    return;
                }

                if (intent == CreateEventSubmitIntent.SaveDraftAndAddSession)
                {
                    Navigation.NavigateTo($"/events/{createdEventId}/sessions/create");
                    return;
                }

                Navigation.NavigateTo($"/event-created/{createdEventId}");
            }
            else
            {
                var errorMsg = response?.Message ?? "Failed to create event.";
                if (response?.Errors != null && response.Errors.Any())
                {
                    errorMsg += " Errors: " + string.Join(", ", response.Errors);
                }
                _submitState.Fail(errorMsg);
            }
        }
        catch (ApiException ex)
        {
            if (!_errorStore.HandleApiError(ex))
            {
                Logger.LogError(ex, "Exception during event creation");
                _submitState.Fail("Event could not be submitted. Please try again.");
            }
            else
            {
                _submitState.Fail("Please fix the validation errors below.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during event creation");
            _submitState.Fail("Event could not be submitted. Please try again.");
        }
    }

    private bool ApplyInlineSessionForIntent(CreateEventSubmitIntent intent)
    {
        createDto.Sessions = new List<CreateEventGraphSessionDto>();

        if (intent != CreateEventSubmitIntent.ReviewAndPublish)
        {
            return true;
        }

        if (!DateTimeHelper.TryCombineDateTimeToUtc(
                _inlineSessionDate,
                _inlineSessionStartTime,
                _selectedTimezone.Id,
                existingInstant: null,
                out DateTimeOffset startTimeUtc,
                out string? scheduleValidationError)
            || !DateTimeHelper.TryCombineDateTimeToUtc(
                _inlineSessionDate,
                _inlineSessionEndTime,
                _selectedTimezone.Id,
                existingInstant: null,
                out DateTimeOffset endTimeUtc,
                out scheduleValidationError))
        {
            _submitState.Fail(scheduleValidationError ?? "Choose the event date, start time, and end time before publishing.");
            return false;
        }

        if (endTimeUtc <= startTimeUtc)
        {
            _submitState.Fail("The event end time must be after the start time.");
            return false;
        }

        createDto.Sessions = new List<CreateEventGraphSessionDto>
        {
            new()
            {
                TempKey = "inline-primary-session",
                Title = string.IsNullOrWhiteSpace(createDto.Title) ? "Main session" : createDto.Title.Trim(),
                Description = createDto.Description,
                StartTime = startTimeUtc,
                EndTime = endTimeUtc,
                LocationId = _inlineSessionLocationId,
                SortOrder = 1,
                MaxAudienceAttendees = _inlineSessionCapacity is > 0 ? _inlineSessionCapacity : null,
                RegistrationModeId = registrationModes?.FirstOrDefault()?.Id,
                LanguageIds = new List<int>()
            }
        };

        return true;
    }

    private async Task SaveAsDraftAsync()
    {
        await SubmitEventAsync(CreateEventSubmitIntent.SaveDraft);
    }


    private async Task ShowPublishReadinessErrorsAsync()
    {
        var firstFieldPath = _publishReadinessErrors.FirstOrDefault()?.FieldPath;
        if (!string.IsNullOrWhiteSpace(firstFieldPath))
        {
            _isMoreOptionsExpanded = true;
        }

        await InvokeAsync(StateHasChanged);
        await AccessibilityFocusService.FocusByIdAsync("publish-readiness-errors");
    }

    private enum CreateEventSubmitIntent
    {
        SaveDraft,
        SaveDraftAndAddSession,
        ReviewAndPublish
    }

    // ========== Helpers ==========

    private string GetPublisherDescription()
    {
        if (_publisherMode == "personal")
            return "Publishing as yourself (User Reported)";

        var publisherId = _publisherMode == "organization" ? _selectedOrganizationId : _selectedGroupId;
        var publisher = GetPublisherOption(_publisherMode, publisherId);
        return publisher?.DisplayName is { Length: > 0 } displayName
            ? $"Publishing for {displayName}"
            : "Select who is publishing this event";
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

    private void HandleCreateTagCatSaved(IReadOnlyCollection<Guid> newIds)
    {
        if (_createTagCatMode == TagCategoryMode.Tags)
            selectedTagIds = new HashSet<Guid>(newIds);
        else
            selectedCategoryIds = new HashSet<Guid>(newIds);

        StateHasChanged();
    }

    private void PopulateCreateRequest()
    {
        createDto.Title = createDto.Title?.Trim() ?? string.Empty;
        createDto.Subtitle = string.IsNullOrWhiteSpace(createDto.Subtitle) ? null : createDto.Subtitle.Trim();
        createDto.Description = string.IsNullOrWhiteSpace(createDto.Description) ? null : createDto.Description.Trim();
        createDto.Content = string.IsNullOrWhiteSpace(createDto.Content) ? null : createDto.Content.Trim();
        createDto.Slug = string.IsNullOrWhiteSpace(createDto.Slug) ? null : createDto.Slug.Trim();
        createDto.CategoryIds = selectedCategoryIds.ToList();
        createDto.TagIds = selectedTagIds.ToList();
        createDto.Locations ??= new List<CreateEventLocationDto>();
        createDto.Sessions ??= new List<CreateEventGraphSessionDto>();
        createDto.Days ??= new List<CreateEventGraphDayDto>();
        createDto.Rooms ??= new List<CreateEventRoomDto>();
        createDto.AgendaItems ??= new List<CreateEventGraphAgendaItemDto>();

    }
}
