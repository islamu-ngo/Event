using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantUsers.Requests.Commands;
using Explore.Application.Features.TenantUsers.Validators;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.TenantUsers.Handlers.Commands;

public sealed class RemoveTenantMembershipCommandHandler(
    ITenantUserRepository tenantUsers,
    ITenantUserRoleGrantRepository roleGrants,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IRequestHandler<RemoveTenantMembershipCommand, bool>
{
    public async Task<bool> Handle(
        RemoveTenantMembershipCommand request,
        CancellationToken cancellationToken)
    {
        await new RemoveTenantMembershipCommandValidator()
            .ValidateAndThrowAsync(request, cancellationToken);

        var actorUserId = currentUser.UserId;
        if (!actorUserId.HasValue || tenantContext.TenantId != request.TenantId)
        {
            throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Update);
        }

        var removedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (actorUserId.Value != request.UserId
                && !await roleGrants.IsTenantAdminInCurrentTenantAsync(request.TenantId, actorUserId.Value, ct))
            {
                throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Update);
            }

            return await tenantUsers.TryRemoveMembershipAsync(
                request.TenantId,
                request.UserId,
                actorUserId.Value,
                removedAtUtc,
                ct);
        }, cancellationToken);
    }
}
