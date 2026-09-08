using Explore.Blazor.Client.Contracts.Services.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services;

public sealed class NotificationRefreshStreamClient : INotificationRefreshStreamClient
{
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<NotificationRefreshStreamClient> _logger;
    private IJSObjectReference? _module;
    private DotNetObjectReference<NotificationRefreshStreamClient>? _dotNetReference;
    private bool _started;

    public NotificationRefreshStreamClient(
        IJSRuntime jsRuntime,
        NavigationManager navigationManager,
        ILogger<NotificationRefreshStreamClient> logger)
    {
        _jsRuntime = jsRuntime;
        _navigationManager = navigationManager;
        _logger = logger;
    }

    public event Func<NotificationRefreshHintReceivedEventArgs, Task>? RefreshReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        try
        {
            _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "/js/notification-refresh.js");

            _dotNetReference ??= DotNetObjectReference.Create(this);
            var streamUrl = _navigationManager.ToAbsoluteUri("api/notification/stream").ToString();

            await _module.InvokeVoidAsync(
                "startNotificationRefresh",
                cancellationToken,
                streamUrl,
                _dotNetReference);

            _started = true;
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected during startup; polling remains the fallback.
        }
        catch (JSException ex)
        {
            _logger.LogDebug(ex, "Notification refresh SSE startup failed; polling fallback remains active");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Notification refresh SSE startup was deferred because JavaScript interop is unavailable");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started || _module is null)
            return;

        try
        {
            await _module.InvokeVoidAsync("stopNotificationRefresh", cancellationToken);
        }
        catch (JSDisconnectedException)
        {
            // Browser/circuit is already gone.
        }
        catch (JSException ex)
        {
            _logger.LogDebug(ex, "Notification refresh SSE stop failed during cleanup");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Notification refresh SSE stop skipped because JavaScript interop is unavailable");
        }
        finally
        {
            _started = false;
        }
    }

    [JSInvokable]
    public async Task HandleNotificationRefresh(
        int unreadCount,
        bool hasUnread,
        string? reason,
        string? generatedAt)
    {
        var parsedGeneratedAt = DateTimeOffset.TryParse(generatedAt, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        var handler = RefreshReceived;
        if (handler is null)
            return;

        await handler(new NotificationRefreshHintReceivedEventArgs(
            unreadCount,
            hasUnread,
            string.IsNullOrWhiteSpace(reason) ? "refresh" : reason,
            parsedGeneratedAt));
    }

    [JSInvokable]
    public Task HandleNotificationRefreshError()
    {
        _logger.LogDebug("Notification refresh SSE connection reported an error; browser reconnect and polling fallback remain active");
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task HandleWebPushRefresh()
    {
        var handler = RefreshReceived;
        if (handler is null)
        {
            return;
        }

        await handler(new NotificationRefreshHintReceivedEventArgs(
            -1,
            true,
            "web-push",
            DateTimeOffset.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        _dotNetReference?.Dispose();

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Browser/circuit is already gone.
            }
        }
    }
}
