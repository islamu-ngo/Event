using Explore.Application.DTOs.Notification;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetUserNotificationsRequest : IRequest<PaginatedResult<NotificationListDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Optional filter: null = all, true = read only, false = unread only.
    /// </summary>
    public bool? IsRead { get; init; }

    /// <summary>
    /// Optional filter by notification type ID (lookup table FK).
    /// </summary>
    public int? NotificationTypeId { get; init; }

    /// <summary>
    /// Optional filter by notification scope (ActorType FK: User=1/Personal, Organization=2, Group=4, System=5).
    /// </summary>
    public int? NotificationScopeId { get; init; }

    public int? NotificationReasonId { get; init; }

    /// <summary>
    /// Optional filter: null = all, true = archived only, false = non-archived only.
    /// </summary>
    public bool? IsArchived { get; init; }

    /// <summary>
    /// Optional filter: null = all, true = currently snoozed, false = not snoozed.
    /// </summary>
    public bool? IsSnoozed { get; init; }
}
