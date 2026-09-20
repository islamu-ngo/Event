using Explore.Application.DTOs.Notification;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetUnreadCountQuery : IQuery<UnreadCountDto>
{
    /// <summary>
    /// Optional filter by notification scope (ActorType FK: User=1/Personal, Organization=2, Group=4, System=5).
    /// </summary>
    public int? NotificationScopeId { get; init; }
}
