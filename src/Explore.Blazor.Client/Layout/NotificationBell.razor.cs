using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Notifications;
using Explore.Blazor.Client.Helpers;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Layout;

public partial class NotificationBell : IAsyncDisposable
{
    private const int PageSize = 20;
    private const int PollIntervalMs = 60_000;

    [Inject]
    private INotificationService NotificationService { get; set; } = null!;

    [Inject]
    private INotificationRefreshStreamClient NotificationRefreshStreamClient { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [Inject]
    private ILogger<NotificationBell> Logger { get; set; } = null!;

    private int _unreadCount;
    private bool _panelOpen;
    private bool _isLoading;
    private bool _hasMore;
    private int _currentPage = 1;
    private int? _selectedScope;
    private readonly List<NotificationListDto> _notifications = [];
    private Timer? _pollTimer;

    private string BadgeContent => _unreadCount > 99 ? "99+" : _unreadCount.ToString();

    protected override async Task OnInitializedAsync()
    {
        await RefreshUnreadCountAsync();
        _pollTimer = new Timer(async _ => await PollUnreadCountAsync(), null, PollIntervalMs, PollIntervalMs);
        NotificationRefreshStreamClient.RefreshReceived += HandleNotificationRefreshAsync;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await NotificationRefreshStreamClient.StartAsync();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Notification refresh stream startup failed; polling fallback remains active.");
        }
    }

    private async Task PollUnreadCountAsync()
    {
        try
        {
            var count = await NotificationService.GetUnreadCountAsync();
            if (count != _unreadCount)
            {
                _unreadCount = count;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch
        {
            // Silently fail — polling is best-effort
        }
    }

    private async Task RefreshUnreadCountAsync()
    {
        _unreadCount = await NotificationService.GetUnreadCountAsync();
    }

    private async Task HandleNotificationRefreshAsync(NotificationRefreshHintReceivedEventArgs hint)
    {
        await InvokeAsync(async () =>
        {
            if (hint.UnreadCount < 0)
            {
                await RefreshUnreadCountAsync();
            }
            else
            {
                _unreadCount = Math.Max(0, hint.UnreadCount);
            }

            if (_panelOpen)
            {
                _currentPage = 1;
                _notifications.Clear();
                await LoadNotificationsAsync();
            }

            StateHasChanged();
        });
    }

    private async Task TogglePanel()
    {
        if (_panelOpen)
        {
            ClosePanel();
            return;
        }

        _panelOpen = true;
        _isLoading = true;
        _currentPage = 1;
        _notifications.Clear();

        await LoadNotificationsAsync();
    }

    private void ClosePanel()
    {
        _panelOpen = false;
    }

    private async Task LoadNotificationsAsync()
    {
        _isLoading = true;

        try
        {
            var result = await NotificationService.GetNotificationsAsync(_currentPage, PageSize, null, _selectedScope);
            _notifications.AddRange(result.Items);
            _hasMore = result.HasNextPage;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadMoreAsync()
    {
        _currentPage++;
        await LoadNotificationsAsync();
    }

    private async Task HandleScopeChanged(int? scope)
    {
        _selectedScope = scope;
        _currentPage = 1;
        _notifications.Clear();
        await LoadNotificationsAsync();
    }

    private void HandleViewAll()
    {
        ClosePanel();
        Nav.NavigateTo("/notifications");
    }

    private void HandleNotificationClick(NotificationListDto notification)
    {
        ClosePanel();

        var url = NotificationNavigationHelper.GetEntityUrl(notification);
        if (!string.IsNullOrEmpty(url))
        {
            Nav.NavigateTo(url);
        }
    }

    private async Task HandleDeleteNotification(NotificationListDto notification)
    {
        if (notification.Id is null) return;

        var success = await NotificationService.DeleteAsync(notification.Id.Value);
        if (success)
        {
            _notifications.Remove(notification);
        }
    }

    private async Task HandleArchiveNotification(NotificationListDto notification)
    {
        if (notification.Id is null) return;

        var archive = notification.IsArchived != true;
        var success = await NotificationService.ArchiveAsync(notification.Id.Value, archive);
        if (success)
        {
            _notifications.Remove(notification);
        }
    }

    private async Task HandleSnoozeNotification(NotificationListDto notification)
    {
        if (notification.Id is null) return;

        var snoozedUntil = DateTimeOffset.UtcNow.AddHours(3);
        var success = await NotificationService.SnoozeAsync(notification.Id.Value, snoozedUntil);
        if (success)
        {
            _notifications.Remove(notification);
        }
    }

    public async ValueTask DisposeAsync()
    {
        NotificationRefreshStreamClient.RefreshReceived -= HandleNotificationRefreshAsync;
        try
        {
            await NotificationRefreshStreamClient.StopAsync();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Notification refresh stream cleanup failed.");
        }

        _pollTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
