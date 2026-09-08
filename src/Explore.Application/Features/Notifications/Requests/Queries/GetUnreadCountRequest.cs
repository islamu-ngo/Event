using Explore.Application.DTOs.Notification;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetUnreadCountRequest : IRequest<UnreadCountDto>
{
    /// <summary>
    /// Optional filter by notification scope (ActorType FK: User=1/Personal, Organization=2, Group=4, System=5).
    /// </summary>
    public int? NotificationScopeId { get; init; }
}
