using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantUsers.Requests.Commands;
using Explore.Application.Features.TenantUsers.Validators;
using FluentValidation;

namespace Explore.Application.Features.TenantUsers.Handlers.Commands;

public sealed class RemoveTenantMembershipCommandHandler(
    ITenantUserRepository tenantUsers,
    ITenantUserRoleGrantRepository roleGrants,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    Func<IHumanSecuritySourceGate> securityGates) : ICommandHandler<RemoveTenantMembershipCommand, bool>
{
    public async Task<bool> ExecuteAsync(
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

        var securityGate = securityGates();
        await using var ownedGate = securityGate.TryRequireCurrent()
            ? null : await securityGate.AcquireAsync(cancellationToken);
        securityGate.RequireCurrent();
        var removedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        bool removed = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            securityGate.RequireCurrent();
            if (actorUserId.Value != request.UserId
                && !await roleGrants.IsTenantAdminInCurrentTenantAsync(request.TenantId, actorUserId.Value, ct))
            {
                throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Update);
            }

            bool changed = await tenantUsers.TryRemoveMembershipAsync(
                request.TenantId,
                request.UserId,
                actorUserId.Value,
                removedAtUtc,
                ct);
            securityGate.RequireCurrent();
            return changed;
        }, cancellationToken);
        securityGate.RequireCurrent();
        return removed;
    }
}
