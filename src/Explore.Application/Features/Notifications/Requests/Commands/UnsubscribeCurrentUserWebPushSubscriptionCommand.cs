using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record UnsubscribeCurrentUserWebPushSubscriptionCommand(Guid SubscriptionId = default) : ICommand<BaseCommandResponse<Guid>>;
