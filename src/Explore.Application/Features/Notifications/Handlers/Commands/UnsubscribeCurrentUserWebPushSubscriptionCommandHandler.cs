using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Commands;

public sealed class UnsubscribeCurrentUserWebPushSubscriptionCommandHandler(
    IWebPushSubscriptionRepository repository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UnsubscribeCurrentUserWebPushSubscriptionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UnsubscribeCurrentUserWebPushSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SubscriptionId == Guid.Empty)
        {
            return Failure("Subscription id is required.");
        }

        var userId = currentUserService.UserId;
        if (!currentUserService.IsAuthenticated || !userId.HasValue)
        {
            return Failure("User not authenticated.");
        }

        var removed = await repository.UnsubscribeAsync(
            tenantContext.TenantId,
            userId.Value,
            request.SubscriptionId,
            DateTime.UtcNow,
            cancellationToken);

        return removed
            ? BaseCommandResponse.Success(request.SubscriptionId, "Web Push subscription removed.")
            : Failure("Web Push subscription was not found.");
    }

    private static BaseCommandResponse<Guid> Failure(string message) =>
        BaseCommandResponse.Validation<Guid>([message], message);
}
