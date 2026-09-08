using Explore.Application.DTOs.Notification;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetCurrentUserWebPushSubscriptionQuery : IRequest<WebPushSubscriptionDto?>
{
    public required string DeviceIdentifier { get; init; }
}
