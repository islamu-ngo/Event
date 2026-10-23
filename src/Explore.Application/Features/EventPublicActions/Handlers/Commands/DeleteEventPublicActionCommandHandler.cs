using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class DeleteEventPublicActionCommandHandler(
    IEventPublicActionRepository actionRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    RequestAuthorization<DeleteEventPublicActionCommand> authorization,
    ILogger<DeleteEventPublicActionCommandHandler> logger)
    : ICommandHandler<DeleteEventPublicActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        DeleteEventPublicActionCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure(request.ActionId, "Public action could not be deleted.", "An authenticated user is required.");
        }

        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            if (await actionRepository.GetAuthorizedEventForMutationAsync(
                    tenantContext.TenantId, userId, request.EventId, token) is null)
                return BaseCommandResponse.Authorization<Guid>();

            var action = await actionRepository.GetForUpdateAsync(request.ActionId, token);
            if (action is null || action.EventId != request.EventId || action.TenantId != tenantContext.TenantId)
            {
                return Failure(request.ActionId, "Public action could not be deleted.", "Public action was not found for this event.");
            }

            if (request.ExpectedConcurrencyStamp == Guid.Empty || action.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
            {
                return Failure(request.ActionId, "Public action could not be deleted.", "Public action changed since it was loaded.");
            }

            token.ThrowIfCancellationRequested();
            await actionRepository.Delete(action);
            token.ThrowIfCancellationRequested();
            return BaseCommandResponse.Success(action.Id, "Public action deleted.");
        }, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, string error) =>
        BaseCommandResponse.Validation<Guid>([error], message, id);
}
