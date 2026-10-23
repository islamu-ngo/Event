using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class UpdateFooterLinkGroupCommandHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    IMachinePrincipalAccessor machinePrincipal,
    IMachineMutationCommitAuthority machineAuthority,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    ITenantAdministratorCommitAuthority tenantAuthority,
    ISystemSettingRepository systemSettings,
    IDeploymentModeProvider deploymentMode,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    RequestAuthorization<UpdateFooterLinkGroupCommand> authorization,
    IFooterOperationExecution footerExecution,
    ILogger<UpdateFooterLinkGroupCommandHandler> logger)
    : ICommandHandler<UpdateFooterLinkGroupCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateFooterLinkGroupCommand request, CancellationToken cancellationToken)
    {
        var human = await footerExecution.RequireOwnedAsync(request, cancellationToken);
        var validator = new PatchFooterLinkGroupDtoValidator();
        await validator.ValidateAndThrowAsync(request.Update, cancellationToken);

        Guid tenantId = tenantContext.TenantId;
        Guid? humanId = await adminContext.ResolveUserIdAsync(cancellationToken);
        bool machine = machinePrincipal.IsMachineCaller;
        if (!userContext.IsAuthenticated || tenantId == Guid.Empty || !machine && humanId is null)
            return BaseCommandResponse.Authentication<Guid>();
        var authoritative = request with { TenantId = tenantId };
        async Task<BaseCommandResponse<Guid>> Update(CancellationToken transactionToken)
        {
            await authorization.AuthorizeAsync(authoritative, logger, transactionToken);
            bool allowed = machine
                ? await machineAuthority.AuthorizeFooterPatchAsync(tenantId, request.GroupId, null, transactionToken)
                : humanId is { } actor
                    && (await instanceAuthority.IsInstanceAdministratorForCommitAsync(actor, transactionToken)
                        || await tenantAuthority.IsTenantAdministratorForCommitAsync(actor, tenantId, transactionToken));
            if (!allowed)
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            if (!await deploymentMode.IsSingleTenantAsync(transactionToken))
            {
                var current = await systemSettings.GetByKey(FooterSettingDefinitions.LockTenantLinkGroups.Key, transactionToken);
                if (!bool.TryParse(current?.Value ?? FooterSettingDefinitions.LockTenantLinkGroups.DefaultValue, out bool locked) || locked)
                    throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            }
            var group = await footerLinkGroupRepository.GetForMutationAsync(request.GroupId, transactionToken);
            if (group is null)
                throw new NotFoundException("footer group", request.GroupId);
            if (machine && !await machineAuthority.AuthorizeFooterPatchAsync(tenantId, group.Id, null, transactionToken))
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            if (request.Update.Title is not null)
                group.Title = request.Update.Title.Value.Trim();
            if (request.Update.IsActive?.Value is bool isActive)
                group.IsActive = isActive;
            await footerLinkGroupRepository.Update(group);
            if (machine && !await machineAuthority.AuthorizeFooterPatchAsync(tenantId, group.Id, null, transactionToken))
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            return BaseCommandResponse.Success(group.Id, "Footer link group updated successfully.");
        }
        if (human is not null)
            return await Update(cancellationToken);
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[FooterSettingDefinitions.LockTenantLinkGroups.Key]],
            token => unitOfWork.ExecuteSerializableAsync(Update, token),
            cancellationToken);
    }
}
