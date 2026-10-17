using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using FluentValidation;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Footer.Handlers.Commands;

public sealed class UpdateFooterLinkCommandHandler(
    IFooterLinkRepository footerLinkRepository,
    ITenantContext tenantContext,
    IHierarchicalSettingsResolver settingsResolver,
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
    RequestAuthorization<UpdateFooterLinkCommand> authorization,
    IFooterOperationExecution footerExecution,
    ILogger<UpdateFooterLinkCommandHandler> logger)
    : ICommandHandler<UpdateFooterLinkCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateFooterLinkCommand request, CancellationToken cancellationToken)
    {
        var human = await footerExecution.RequireOwnedAsync(request, cancellationToken);
        bool requireHttps = await settingsResolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Security.RequireHttpsExternalUrls,
            new SettingContext(),
            cancellationToken);
        var validator = new PatchFooterLinkDtoValidator(requireHttps);
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
            var candidate = await footerLinkRepository.GetByIdForTenantAsync(request.LinkId, tenantId, transactionToken);
            if (candidate is null)
                throw new NotFoundException("footer link", request.LinkId);
            Guid groupId = candidate.FooterLinkGroupId;
            bool allowed = machine
                ? await machineAuthority.AuthorizeFooterPatchAsync(tenantId, groupId, request.LinkId, transactionToken)
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
            var link = await footerLinkRepository.GetForMutationAsync(request.LinkId, groupId, transactionToken);
            if (link is null || link.FooterLinkGroupId != groupId)
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            if (machine && !await machineAuthority.AuthorizeFooterPatchAsync(tenantId, groupId, link.Id, transactionToken))
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            if (request.Update.Label is not null)
                link.Label = request.Update.Label.Value.Trim();
            if (request.Update.Url is not null)
                link.Url = request.Update.Url.Value.Trim();
            if (request.Update.OpenInNewTab?.Value is bool openInNewTab)
                link.OpenInNewTab = openInNewTab;
            if (request.Update.IsActive?.Value is bool isActive)
                link.IsActive = isActive;
            await footerLinkRepository.Update(link);
            if (machine && !await machineAuthority.AuthorizeFooterPatchAsync(tenantId, groupId, link.Id, transactionToken))
                throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Update);
            return BaseCommandResponse.Success(link.Id, "Footer link updated successfully.");
        }
        if (human is not null)
            return await Update(cancellationToken);
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[FooterSettingDefinitions.LockTenantLinkGroups.Key]],
            token => unitOfWork.ExecuteSerializableAsync(Update, token),
            cancellationToken);
    }
}
