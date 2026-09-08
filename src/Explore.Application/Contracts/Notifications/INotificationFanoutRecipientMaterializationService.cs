using Explore.Domain;

namespace Explore.Application.Contracts.Notifications;

public interface INotificationFanoutRecipientMaterializationService
{
    Task<RecipientNotificationMaterializationResult> MaterializeAsync(
        NotificationFanoutOccurrence occurrence,
        Guid recipientUserId,
        CancellationToken cancellationToken = default);
}
