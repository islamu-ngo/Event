using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wire = ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Blazor.Client.Layout;

public partial class AnnouncementBar : IDisposable
{
    private bool _isVisible;
    private bool _canDismiss;
    private Wire.AnnouncementObservationDto? _observation;
    private string _message = string.Empty;
    private string _linkText = string.Empty;
    private string _linkUrl = string.Empty;
    private long _generation;
    private bool _disposed;
    private bool _isSaving;
    private bool _isLoading = true;
    private bool _saveFailed;

    [Inject]
    private IPublicExperienceService PublicExperienceService { get; set; } = null!;

    [Inject]
    private IAnnouncementClientService Announcements { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private bool HasLink => !string.IsNullOrWhiteSpace(_linkText) && !string.IsNullOrWhiteSpace(_linkUrl);
    private string? LinkTarget => IsExternalLink(_linkUrl) ? "_blank" : null;
    private string? LinkRel => IsExternalLink(_linkUrl) ? "noopener noreferrer" : null;

    /// <summary>Updates the layout's header height when the bar's visibility changes.</summary>
    [Parameter]
    public EventCallback<bool> OnVisibilityChanged { get; set; }

    protected override void OnInitialized()
    {
        PublicExperienceService.SettingsChanged += OnPublicExperienceSettingsChanged;
        AuthenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadSettingsAsync();
            if (!_disposed)
                StateHasChanged();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _generation);
        PublicExperienceService.SettingsChanged -= OnPublicExperienceSettingsChanged;
        AuthenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> _) => OnPublicExperienceSettingsChanged();

    private void OnPublicExperienceSettingsChanged()
    {
        Interlocked.Increment(ref _generation);
        _isLoading = true;
        _ = InvokeAsync(async () =>
        {
            if (_disposed)
                return;
            await LoadSettingsAsync();
            if (!_disposed)
                StateHasChanged();
        });
    }

    private async Task LoadSettingsAsync(bool reconcile = false)
    {
        long generation = Interlocked.Increment(ref _generation);
        _isSaving = false;
        _saveFailed = false;
        var state = PublicExperienceService.SettingsAvailability == PublicExperienceAvailability.Unavailable
            ? null : await Announcements.LoadAsync(reconcile);
        if (_disposed || generation != Volatile.Read(ref _generation))
            return;

        bool wasVisible = _isVisible;
        _message = state?.Material?.Message ?? string.Empty;
        _linkText = state?.Material?.LinkText ?? string.Empty;
        _linkUrl = state?.Material?.LinkUrl ?? string.Empty;
        _observation = state?.Observation;
        _canDismiss = state?.CanDismiss == true;
        _isSaving = Announcements.IsSaving;
        _saveFailed = Announcements.RequiresReconciliation;
        _isLoading = false;
        _isVisible = state?.IsVisible == true && !state.IsDismissed && !string.IsNullOrWhiteSpace(_message);
        if (_isVisible != wasVisible)
            await OnVisibilityChanged.InvokeAsync(_isVisible);
    }

    private async Task CloseBar()
    {
        if (_disposed || _isLoading || _isSaving || !_isVisible || !_canDismiss)
            return;
        long generation = Volatile.Read(ref _generation);
        var observed = _observation;
        _isSaving = true;
        _saveFailed = false;
        bool saved = await Announcements.DismissAsync();
        if (_disposed)
            return;
        if (generation != Volatile.Read(ref _generation))
        {
            var refreshed = Announcements.Display;
            if (refreshed is not null
                && AnnouncementClientService.SameIdentity(refreshed.Observation, _observation))
            {
                _isSaving = Announcements.IsSaving;
                _saveFailed = Announcements.RequiresReconciliation;
                _canDismiss = refreshed.CanDismiss && !_saveFailed;
            }
            return;
        }

        _isSaving = false;
        var state = Announcements.Display;
        if (!saved || state?.IsDismissed != true
            || !AnnouncementClientService.SameIdentity(state.Observation, observed))
        {
            _saveFailed = true;
            _canDismiss = state?.CanDismiss == true && !Announcements.RequiresReconciliation;
            return;
        }
        _isVisible = false;
        await OnVisibilityChanged.InvokeAsync(false);
    }

    private Task ReconcileAnnouncementAsync() => LoadSettingsAsync(reconcile: true);

    private static bool IsExternalLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}
