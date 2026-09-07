using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public sealed class GetCurrentUserWebPushSubscriptionQueryHandler(
    IWebPushSubscriptionRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrentUserWebPushSubscriptionQuery, WebPushSubscriptionDto?>
{
    public async Task<WebPushSubscriptionDto?> Handle(
        GetCurrentUserWebPushSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceIdentifier))
        {
            return null;
        }

        var userId = currentUserService.UserId;
        if (!currentUserService.IsAuthenticated || !userId.HasValue)
        {
            return null;
        }

        var subscription = await repository.GetActiveForDeviceAsync(
            tenantContext.TenantId,
            userId.Value,
            request.DeviceIdentifier,
            cancellationToken);

        return subscription is null ? null : ToDto(subscription);
    }

    private static WebPushSubscriptionDto ToDto(WebPushSubscription subscription) => new()
    {
        Id = subscription.Id,
        DeviceIdentifier = subscription.DeviceIdentifier,
        LastSeenAt = subscription.LastSeenAt,
        ExpirationTime = subscription.ExpirationTime
    };
}
