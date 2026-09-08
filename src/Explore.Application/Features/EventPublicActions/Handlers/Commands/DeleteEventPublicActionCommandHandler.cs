using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class DeleteEventPublicActionCommandHandler(
    IEventPublicActionRepository actionRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteEventPublicActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        DeleteEventPublicActionCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is null)
        {
            return Failure(request.ActionId, "Public action could not be deleted.", "An authenticated user is required.");
        }

        var action = await actionRepository.GetForUpdateAsync(request.ActionId, cancellationToken);
        if (action is null || action.EventId != request.EventId || action.TenantId != tenantContext.TenantId)
        {
            return Failure(request.ActionId, "Public action could not be deleted.", "Public action was not found for this event.");
        }

        if (request.ExpectedConcurrencyStamp == Guid.Empty || action.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            return Failure(request.ActionId, "Public action could not be deleted.", "Public action changed since it was loaded.");
        }

        await actionRepository.Delete(action);
        return BaseCommandResponse.Success(action.Id, "Public action deleted.");
    }

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, string error) =>
        BaseCommandResponse.Validation<Guid>([error], message, id);
}
