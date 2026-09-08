using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Notifications;

public interface IActorSubscriptionService
{
    Task<ActorSubscriptionDto?> GetSubscriptionAsync(Guid targetActorId, CancellationToken cancellationToken = default);

    Task<ActorSubscriptionCommandResult> SubscribeAsync(Guid targetActorId, CancellationToken cancellationToken = default);

    Task<ActorSubscriptionCommandResult> UnsubscribeAsync(Guid targetActorId, Guid expectedConcurrencyStamp, CancellationToken cancellationToken = default);

    Task<ActorSubscriptionCommandResult> UpdateNotificationLevelAsync(
        Guid targetActorId,
        int notificationLevelId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default);
}
