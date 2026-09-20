using Explore.Application.DTOs.Notification;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetCurrentUserWebPushSubscriptionQuery : IQuery<WebPushSubscriptionDto?>
{
    public required string DeviceIdentifier { get; init; }
}
