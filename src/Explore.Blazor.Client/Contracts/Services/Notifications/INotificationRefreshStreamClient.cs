namespace Explore.Blazor.Client.Contracts.Services.Notifications;

public interface INotificationRefreshStreamClient : IAsyncDisposable
{
    event Func<NotificationRefreshHintReceivedEventArgs, Task>? RefreshReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
