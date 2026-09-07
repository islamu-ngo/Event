using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record UnsubscribeCurrentUserWebPushSubscriptionCommand(Guid SubscriptionId = default) : IRequest<BaseCommandResponse<Guid>>;
