using Explore.Application.Contracts.Operations;

namespace Explore.Application.Notifications;

public static class NotificationHandlerExtensions
{
    /// <summary>
    /// Awaits consumers in registration order, stopping at the first failure.
    /// The producer owns the cancellation token and any committed state.
    /// </summary>
    public static async Task HandleAsync<TNotification>(
        this IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(notification, cancellationToken);
        }
    }
}
