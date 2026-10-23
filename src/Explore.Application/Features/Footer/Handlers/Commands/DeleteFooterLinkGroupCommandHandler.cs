using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class DeleteFooterLinkGroupCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    IFooterLinkRepository footerLinkRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    ITenantAdministratorCommitAuthority tenantAuthority,
    ISystemSettingRepository systemSettings,
    IDeploymentModeProvider deploymentMode,
    ISettingMutationLock mutationLock,
    RequestAuthorization<DeleteFooterLinkGroupCommand> authorization,
    IFooterOperationExecution footerExecution,
    ILogger<DeleteFooterLinkGroupCommandHandler> logger)
    : ICommandHandler<DeleteFooterLinkGroupCommand, bool>
{
    public async Task<bool> ExecuteAsync(
        DeleteFooterLinkGroupCommand request, CancellationToken cancellationToken)
    {
        var human = await footerExecution.RequireOwnedAsync(request, cancellationToken);
        Guid tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty || !userContext.IsAuthenticated
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
        var authoritativeRequest = request with { TenantId = tenantId, UserId = userId };
        async Task<bool> Delete(CancellationToken transactionToken)
        {
            await authorization.AuthorizeAsync(authoritativeRequest, logger, transactionToken);
            if (!await instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, transactionToken)
                && !await tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, transactionToken))
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            if (!await deploymentMode.IsSingleTenantAsync(transactionToken))
            {
                var current = await systemSettings.GetByKey(FooterSettingDefinitions.LockTenantLinkGroups.Key, transactionToken);
                string value = current?.Value ?? FooterSettingDefinitions.LockTenantLinkGroups.DefaultValue;
                if (!bool.TryParse(value, out bool locked) || locked)
                    throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            }
            var group = await footerLinkGroupRepository.GetForMutationAsync(request.GroupId, transactionToken);
            if (group is null)
                throw new NotFoundException("footer group", request.GroupId);
            await footerLinkRepository.FenceByGroupIdAsync(group.Id, transactionToken);
            await footerLinkRepository.DeleteByGroupIdAsync(group.Id, transactionToken);
            await footerLinkGroupRepository.Delete(group);
            transactionToken.ThrowIfCancellationRequested();
            return true;
        }
        if (human is not null)
            return await Delete(cancellationToken);
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[FooterSettingDefinitions.LockTenantLinkGroups.Key]],
            token => unitOfWork.ExecuteSerializableAsync(Delete, token),
            cancellationToken);
    }
}
