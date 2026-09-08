using Explore.Application.Notifications;

namespace Explore.Application.Contracts.Notifications;

public interface INotificationOwnershipResolver
{
    Task<NotificationOwnershipDecision> ResolveAsync(
        NotificationIntentDraft draft,
        CancellationToken cancellationToken = default);
}
