namespace Explore.Blazor.Client.Services;

public sealed class AiAssistantState
{
    private bool _tenantEnabled;
    private bool _tenantAvailable;
    private bool _allowAnonymousAccess;
    private bool _isAuthenticated;
    private bool _showNavbarButton = true;

    public bool IsOpen { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool IsButtonVisible { get; private set; }
    public bool CanOpenWorkspace => _isAuthenticated && IsAvailable;
    public event Action? OnChange;

    /// <summary>
    /// Applies tenant policy and current viewer context. The effective availability remains
    /// constrained by the user's personal navbar preference.
    /// </summary>
    public void SetPolicy(bool tenantEnabled, bool tenantAvailable, bool allowAnonymousAccess, bool isAuthenticated)
    {
        _tenantEnabled = tenantEnabled;
        _tenantAvailable = tenantAvailable;
        _allowAnonymousAccess = allowAnonymousAccess;
        _isAuthenticated = isAuthenticated;
        RecomputeAvailability();
    }

    public void SetUserNavbarPreference(bool showNavbarButton)
    {
        _showNavbarButton = showNavbarButton;
        RecomputeAvailability();
    }

    public void Toggle()
    {
        if (!IsAvailable) return;
        IsOpen = !IsOpen;
        OnChange?.Invoke();
    }

    public void Open()
    {
        if (!IsAvailable || IsOpen) return;
        IsOpen = true;
        OnChange?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnChange?.Invoke();
    }

    private void RecomputeAvailability()
    {
        var audienceAllowed = (_isAuthenticated || _allowAnonymousAccess) && _showNavbarButton;
        var available = _tenantEnabled && _tenantAvailable && audienceAllowed;
        var buttonVisible = available;

        if (IsAvailable == available && IsButtonVisible == buttonVisible)
        {
            return;
        }

        IsAvailable = available;
        IsButtonVisible = buttonVisible;
        if (!available)
        {
            IsOpen = false;
        }

        OnChange?.Invoke();
    }
}
