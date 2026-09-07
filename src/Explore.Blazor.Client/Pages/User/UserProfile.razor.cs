using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Pages.User;

/// <summary>
/// Code-behind for the User Profile page.
/// Displays user information, event attendance stats, and reviews.
/// </summary>
public partial class UserProfile : ComponentBase, IDisposable
{
    [Inject] private IUserService UserService { get; set; } = default!;
    [Inject] private IEventService EventService { get; set; } = default!;
    [Inject] private IOrganizationReviewService OrganizationReviewService { get; set; } = default!;
    [Inject] private IUserSettingsService UserSettingsService { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    [Inject] private ILogger<UserProfile> Logger { get; set; } = default!;

    private const string DefaultBannerFallback = "#6366f1";

    // State
    private UserDto? UserData { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }
    private string _bannerStyle = AppearanceStyleBuilder.BuildBannerStyle(new AppearanceSettings(), DefaultBannerFallback);

    // Statistics
    private int EventsAttended { get; set; }
    private int ReviewsGiven { get; set; }
    private ICollection<OrganizationReviewDto> MyReviews { get; set; } = new List<OrganizationReviewDto>();
    private ICollection<EventListDto> _posts = new List<EventListDto>();
    private SettingGroupResponseDto? _atprotoSettings;
    private bool _isLoadingAtprotoSettings;
    private bool _isSavingAtprotoConsent;
    private bool _atprotoSaveSucceeded;
    private string? _atprotoMessage;
    private readonly CancellationTokenSource _lifetime = new();
    private EventPreviewWorkspace? _eventPreviewWorkspace;

    private const string AtprotoCategory = "AtprotoFederation";
    private const string AtprotoEventsEnabledKey = "federation.atproto_events_enabled";
    private const string AtprotoPublishMyEventsKey = "federation.atproto_publish_my_events";

    protected override async Task OnInitializedAsync()
    {
        Logger.LogInformation("[UserProfile] OnInitializedAsync starting...");
        await LoadUserDataAsync();
    }

    private async Task LoadUserDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Logger.LogInformation("[UserProfile] Loading user data...");

            // First, try to get the user from the API
            var userData = await UserService.GetCurrentUserAsync();

            if (userData == null)
            {
                Logger.LogWarning("[UserProfile] User data not found, attempting sync...");

                // Sync user from Keycloak to local database
                var syncResult = await UserService.SyncUserAsync();

                if (syncResult?.Success == true)
                {
                    Logger.LogInformation("[UserProfile] User synced successfully, retrying load...");
                    // Small delay to ensure database write is complete
                    await Task.Delay(200);
                    userData = await UserService.GetCurrentUserAsync();
                }
                else
                {
                    Logger.LogWarning("[UserProfile] User sync failed: {Message}", syncResult?.Message ?? "Unknown error");
                }
            }

            if (userData != null)
            {
                UserData = userData;
                Logger.LogInformation("[UserProfile] User data loaded: {Email}", UserData.Email);

                _bannerStyle = AppearanceStyleBuilder.BuildBannerStyle(
                    new AppearanceSettings
                    {
                        BackgroundColor = userData.ActorBackgroundColor ?? string.Empty,
                        ImageUri = userData.ActorBackgroundImageUri ?? string.Empty,
                        BackgroundEffect = userData.ActorBackgroundEffect ?? string.Empty
                    },
                    DefaultBannerFallback);

                // Load statistics in parallel
                if (userData.Id.HasValue)
                {
                    await LoadStatisticsAsync(userData.Id.Value);
                }
            }
            else
            {
                Logger.LogWarning("[UserProfile] UserData is still null after sync attempt");
                ErrorMessage = "Unable to load user profile. Please try refreshing the page.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[UserProfile] Error loading user data");
            ErrorMessage = "Unable to load your profile. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadStatisticsAsync(Guid userId)
    {
        var reviewsTask = LoadReviewsAsync(userId);
        var postsTask = LoadPostsAsync();
        var atprotoSettingsTask = LoadAtprotoSettingsAsync();

        await Task.WhenAll(reviewsTask, postsTask, atprotoSettingsTask);
    }

    private async Task LoadPostsAsync()
    {
        try
        {
            _posts = await EventService.GetMyEventsAsync();
            Logger.LogInformation("[UserProfile] Loaded {Count} posts", _posts.Count);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[UserProfile] Error loading posts");
            _posts = new List<EventListDto>();
        }
    }

    private async Task LoadReviewsAsync(Guid userId)
    {
        try
        {
            MyReviews = await OrganizationReviewService.GetReviewsByUserId(userId);
            ReviewsGiven = MyReviews?.Count ?? 0;
            Logger.LogInformation("[UserProfile] Loaded {Count} reviews", ReviewsGiven);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[UserProfile] Error loading reviews");
            MyReviews = new List<OrganizationReviewDto>();
            ReviewsGiven = 0;
        }
    }

    private async Task LoadAtprotoSettingsAsync()
    {
        _isLoadingAtprotoSettings = true;
        _atprotoMessage = null;
        try
        {
            _atprotoSettings = await UserSettingsService.GetSettingsAsync(AtprotoCategory, _lifetime.Token);
            if (_atprotoSettings is null)
            {
                _atprotoMessage = "AT Protocol event preferences are temporarily unavailable.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[UserProfile] Error loading AT Protocol event preferences");
            _atprotoSettings = null;
            _atprotoMessage = "AT Protocol event preferences are temporarily unavailable.";
        }
        finally
        {
            _isLoadingAtprotoSettings = false;
        }
    }

    private async Task SetAtprotoPublicationConsentAsync(bool enabled)
    {
        if (_isSavingAtprotoConsent || AtprotoConsentSetting?.CanEdit != true)
        {
            return;
        }

        _isSavingAtprotoConsent = true;
        _atprotoMessage = null;
        try
        {
            _atprotoSaveSucceeded = await UserSettingsService.UpdateSettingAsync(
                AtprotoPublishMyEventsKey,
                enabled ? "true" : "false",
                _lifetime.Token);
            _atprotoMessage = _atprotoSaveSucceeded
                ? "AT Protocol publication preference saved."
                : "AT Protocol publication preference could not be saved.";
            if (_atprotoSaveSucceeded)
            {
                _atprotoSettings = await UserSettingsService.GetSettingsAsync(AtprotoCategory, _lifetime.Token);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[UserProfile] Error saving AT Protocol publication preference");
            _atprotoSaveSucceeded = false;
            _atprotoMessage = "AT Protocol publication preference could not be saved.";
        }
        finally
        {
            _isSavingAtprotoConsent = false;
        }
    }

    private EffectiveSettingDto? AtprotoConsentSetting => FindAtprotoSetting(AtprotoPublishMyEventsKey);

    private bool AtprotoEventsEnabled => ReadSettingBool(FindAtprotoSetting(AtprotoEventsEnabledKey)?.Value);

    private bool PublishMyEvents => ReadSettingBool(AtprotoConsentSetting?.Value);

    private EffectiveSettingDto? FindAtprotoSetting(string key) =>
        _atprotoSettings?.Settings?.FirstOrDefault(setting =>
            string.Equals(setting.Key, key, StringComparison.Ordinal));

    private static bool ReadSettingBool(string? value) =>
        bool.TryParse(value?.Trim().Trim('"'), out bool parsed) && parsed;

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private Task HandleEventSelected(EventListDto evt) =>
        _eventPreviewWorkspace?.SelectEventAsync(evt) ?? Task.CompletedTask;

    private void HandleEventEdit(EventListDto evt)
    {
        _eventPreviewWorkspace?.NavigateToEdit(evt);
    }

    private Task HandleEventDelete(EventListDto evt) =>
        _eventPreviewWorkspace?.OpenDeleteDialogAsync(evt) ?? Task.CompletedTask;

    private Task HandleEventShare(EventListDto evt) =>
        _eventPreviewWorkspace?.ShareEventAsync(evt) ?? Task.CompletedTask;

    private Task HandlePostDeleted(EventListDto evt)
    {
        _posts = _posts.Where(post => post.Id != evt.Id).ToList();
        return Task.CompletedTask;
    }

    private static List<KeyValuePair<DateTime, List<EventListDto>>> GroupEventsByDate(IEnumerable<EventListDto> events)
    {
        return events
            .GroupBy(evt => evt.FirstSessionDate?.Date ?? DateTime.MinValue)
            .OrderByDescending(group => group.Key)
            .Select(group => new KeyValuePair<DateTime, List<EventListDto>>(group.Key, group.ToList()))
            .ToList();
    }

    /// <summary>
    /// Gets the display location from user data.
    /// Returns placeholder if location is not available.
    /// </summary>
    private string GetDisplayLocation()
    {
        // Location is not currently available in UserDto
        // This can be extended when location support is added
        return "Location not set";
    }

    /// <summary>
    /// Gets the full name of the user.
    /// Falls back to username or "User" if name not available.
    /// </summary>
    private string GetFullName()
    {
        if (UserData == null) return "User";

        var firstName = UserData.FirstName?.Trim() ?? string.Empty;
        var lastName = UserData.LastName?.Trim() ?? string.Empty;

        var fullName = $"{firstName} {lastName}".Trim();

        if (!string.IsNullOrEmpty(fullName))
            return fullName;

        return UserData.ActorHandle ?? "User";
    }

    /// <summary>
    /// Gets the initials for the avatar display.
    /// </summary>
    private string GetInitials()
    {
        return DisplayHelper.GetInitials(UserData?.FirstName, UserData?.LastName, UserData?.ActorHandle);
    }

    /// <summary>
    /// Gets the username for display.
    /// </summary>
    private string GetUsername()
    {
        if (UserData == null) return "user";
        return UserData.ActorHandle ?? UserData.Email?.Split('@').FirstOrDefault() ?? "user";
    }
}
