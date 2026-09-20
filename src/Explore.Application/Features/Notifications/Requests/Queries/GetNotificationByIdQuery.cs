using Explore.Application.DTOs.Notification;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetNotificationByIdQuery(Guid Id = default) : IQuery<NotificationDto?>;
