using Explore.Application.Notifications;

namespace Explore.Application.Contracts.Notifications;

public interface INotificationOrchestrator
{
    Task<NotificationOrchestrationResult> EnqueueAsync(
        NotificationIntentDraft draft,
        CancellationToken cancellationToken = default);
}
