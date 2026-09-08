
using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Queries;

public sealed class PreviewEmailDeliveryDisableQueryHandler(
    IAdminContext adminContext,
    ITenantContext tenantContext,
    IEmailDeliveryDisableImpactReader impactReader,
    IEmailDeliveryDisableTokenService tokenService,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IPlatformUserRoleRepository platformRoles,
    ITenantUserRoleGrantRepository tenantRoles)
    : IRequestHandler<PreviewEmailDeliveryDisableQuery, BaseCommandResponse<EmailDeliveryDisablePreviewDto>>
{
    public async Task<BaseCommandResponse<EmailDeliveryDisablePreviewDto>> Handle(
        PreviewEmailDeliveryDisableQuery request,
        CancellationToken cancellationToken)
    {
        if (await ResolveAuthorizedActorAsync(request.TenantId, cancellationToken) is null)
            return BaseCommandResponse.Authorization<EmailDeliveryDisablePreviewDto>(
                "Current administrator authority is required for the selected scope.");
        if (request.TenantId == Guid.Empty)
            return BaseCommandResponse.Validation<EmailDeliveryDisablePreviewDto>(["TenantId must not be empty."]);

        return await mutationLock.ExecuteOrderedGroupsAsync(
            [EmailSettingGroup.SettingKeys.Append(GovernanceSettingKeys.TenantDelegation.LockSmtp)],
            token => unitOfWork.ExecuteSerializableAsync(async transactionToken =>
            {
                Guid? actorId = await ResolveAuthorizedActorAsync(request.TenantId, transactionToken);
                if (actorId is null)
                {
                    return BaseCommandResponse.Authorization<EmailDeliveryDisablePreviewDto>(
                        "Current administrator authority is required for the selected scope.");
                }

                var snapshot = await impactReader.ReadAsync(request.TenantId, transactionToken);
                if (snapshot is null)
                    return BaseCommandResponse.NotFound<EmailDeliveryDisablePreviewDto>("Email delivery scope was not found.");

                var confirmation = snapshot.CanDisable ? tokenService.Issue(actorId.Value, snapshot) : null;
                var preview = new EmailDeliveryDisablePreviewDto(
                    TenantId: snapshot.TenantId,
                    ExpectedRevision: snapshot.Revision,
                    IsLocked: snapshot.IsLocked,
                    AffectedScopes: snapshot.AffectedScopes.Select(scope => new EmailDeliveryDisableAffectedScopeDto(
                        TenantId: scope.TenantId, Revision: scope.Revision)).ToImmutableArray(),
                    ConfirmationToken: confirmation?.Token,
                    ExpiresAtUtc: confirmation?.ExpiresAtUtc);
                return BaseCommandResponse.Success(preview, "Email delivery disable impact retrieved.");
            }, token), cancellationToken);
    }

    private async Task<Guid?> ResolveAuthorizedActorAsync(Guid? targetTenantId, CancellationToken cancellationToken)
    {
        Guid? actorId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (actorId is null || actorId == Guid.Empty)
            return null;
        return await platformRoles.IsUserPlatformAdmin(actorId.Value)
            || (targetTenantId is Guid tenantId && tenantId != Guid.Empty
                && tenantContext.TenantId == tenantId
                && await tenantRoles.IsTenantAdminInCurrentTenantAsync(tenantId, actorId.Value, cancellationToken))
            ? actorId : null;
    }
}
