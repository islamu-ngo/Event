using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Layout;

public partial class AnnouncementBar : IDisposable
{
    private const string UserPreferenceCategory = "PublicExperiencePreferences";
    private const string DismissedRevisionKey = "public_experience_preferences.announcement_bar.dismissed_revision";

    private bool _isVisible;
    private int _announcementRevision;
    private string _message = string.Empty;
    private string _linkText = string.Empty;
    private string _linkUrl = string.Empty;

    [Inject]
    private IPublicExperienceService PublicExperienceService { get; set; } = null!;

    [Inject]
    private IUserSettingsService UserSettingsService { get; set; } = null!;

    private bool HasLink => !string.IsNullOrWhiteSpace(_linkText) && !string.IsNullOrWhiteSpace(_linkUrl);

    private string? LinkTarget => IsExternalLink(_linkUrl) ? "_blank" : null;

    private string? LinkRel => IsExternalLink(_linkUrl) ? "noopener noreferrer" : null;

    /// <summary>
    /// Fires when the announcement bar is shown or dismissed.
    /// The bool parameter is true when visible, false when hidden.
    /// MainLayout uses this to update the theme's AppbarHeight so
    /// --mud-appbar-height on :root reflects the full header height.
    /// </summary>
    [Parameter]
    public EventCallback<bool> OnVisibilityChanged { get; set; }

    protected override async Task OnInitializedAsync()
    {
        PublicExperienceService.SettingsChanged += OnPublicExperienceSettingsChanged;

        await LoadSettingsAsync();
    }

    public void Dispose()
    {
        PublicExperienceService.SettingsChanged -= OnPublicExperienceSettingsChanged;
    }

    private void OnPublicExperienceSettingsChanged()
    {
        _ = InvokeAsync(async () =>
        {
            await LoadSettingsAsync();
            StateHasChanged();
        });
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await PublicExperienceService.GetCachedSettingsAsync();
        var wasVisible = _isVisible;

        _message = settings?.AnnouncementBarMessage?.Trim() ?? string.Empty;
        _linkText = settings?.AnnouncementBarLinkText?.Trim() ?? string.Empty;
        _linkUrl = settings?.AnnouncementBarLinkUrl?.Trim() ?? string.Empty;
        _announcementRevision = settings?.AnnouncementBarRevision ?? 0;

        var dismissedRevision = await GetDismissedRevisionAsync();
        _isVisible = settings?.AnnouncementBarEnabled == true
            && !string.IsNullOrWhiteSpace(_message)
            && dismissedRevision < _announcementRevision;

        if (_isVisible != wasVisible)
        {
            await OnVisibilityChanged.InvokeAsync(_isVisible);
        }
    }

    private async Task CloseBar()
    {
        await UserSettingsService.UpdateSettingsBatchAsync(
            UserPreferenceCategory,
            new Dictionary<string, string>
            {
                [DismissedRevisionKey] = _announcementRevision.ToString(CultureInfo.InvariantCulture)
            });

        _isVisible = false;
        await OnVisibilityChanged.InvokeAsync(false);
    }

    private async Task<int> GetDismissedRevisionAsync()
    {
        // This must be fresh: authenticated users read their DB-backed user preference,
        // while anonymous users fall back to localStorage through IUserSettingsService.
        UserSettingsService.InvalidateCache(UserPreferenceCategory);

        var preferences = await UserSettingsService.GetSettingsAsync(UserPreferenceCategory);
        var dismissedRevision = preferences?.Settings
            .FirstOrDefault(setting => setting.Key == DismissedRevisionKey);

        return ParseRevision(dismissedRevision, -1);
    }

    private static int ParseRevision(EffectiveSettingDto? setting, int fallback)
    {
        if (string.IsNullOrWhiteSpace(setting?.Value))
        {
            return fallback;
        }

        return int.TryParse(
            setting.Value.Trim('"'),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;
    }

    private static bool IsExternalLink(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}
