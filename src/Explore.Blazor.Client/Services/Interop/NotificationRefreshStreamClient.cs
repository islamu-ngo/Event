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
    private bool _stopped;
    private bool _disposed;
    private int _callbackActive;

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
        if (_started || _disposed)
            return;

        try
        {
            _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "/js/notification-refresh.js");

            if (_disposed)
                return;

            _dotNetReference ??= DotNetObjectReference.Create(this);
            var streamUrl = _navigationManager.ToAbsoluteUri("api/notification/stream").ToString();
            _stopped = false;

            await _module.InvokeVoidAsync(
                "startNotificationRefresh",
                cancellationToken,
                streamUrl,
                _dotNetReference);

            _started = true;
        }
        catch (JSDisconnectedException)
        {
            // Browser/circuit is already gone.
        }
        catch (JSException ex)
        {
            _logger.LogDebug(ex, "Notification browser refresh startup failed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Notification refresh SSE startup was deferred because JavaScript interop is unavailable");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopped = true;
        if (_module is null)
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

        await DispatchRefreshAsync(new NotificationRefreshHintReceivedEventArgs(
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
    public Task HandleWebPushRefresh()
    {
        return DispatchRefreshAsync(new NotificationRefreshHintReceivedEventArgs(
            -1,
            true,
            "web-push",
            DateTimeOffset.UtcNow));
    }

    [JSInvokable]
    public Task HandleNotificationPoll()
    {
        return DispatchRefreshAsync(new NotificationRefreshHintReceivedEventArgs(
            -1,
            false,
            "poll",
            DateTimeOffset.UtcNow));
    }

    private async Task DispatchRefreshAsync(NotificationRefreshHintReceivedEventArgs hint)
    {
        // Do not queue callbacks with captured authority across circuit activities.
        if (_stopped || _disposed || Interlocked.CompareExchange(ref _callbackActive, 1, 0) != 0)
            return;

        try
        {
            if (RefreshReceived is { } handlers)
            {
                foreach (Func<NotificationRefreshHintReceivedEventArgs, Task> handler in handlers.GetInvocationList())
                    await handler(hint);
            }
        }
        finally
        {
            Volatile.Write(ref _callbackActive, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
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
