using Explore.Application.DTOs.Notification;

namespace Explore.Application.Contracts.Services;

public interface INotificationRefreshStreamService
{
    IAsyncEnumerable<NotificationRefreshHintDto> StreamAsync(CancellationToken cancellationToken = default);
}
